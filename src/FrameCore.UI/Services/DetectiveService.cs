using System.Diagnostics;
using FrameCore.BenchmarkManager.Detective;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.StartupManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.ViewModels;
using FrameCore.WindowsOptimization.Executors;

namespace FrameCore.UI.Services;

/// <summary>
/// The full investigation: hardware and storage, every running process (classified and measured), internal latency
/// per driver, network path, stability history, comparison with the first state of the PC, stutter analysis.
/// Nothing is changed by the investigation itself; fixes are proposed afterwards and confirmed by the user.
/// </summary>
public sealed class DetectiveService
{
    private readonly ShellViewModel _shell;
    private ProcessClassifier? _classifier;
    public DetectiveStore Store { get; } = new();
    public DetectiveReport? Last { get; private set; }

    public DetectiveService(ShellViewModel shell) { _shell = shell; Last = Store.Latest(); }

    public ProcessClassifier Classifier => _classifier ??= ProcessClassifier.Load(Path.Combine(AppPaths.CatalogDir, "knowledge"));

    public static readonly string[] StepKeys = { "hardware", "processes", "latency", "network", "stability", "history" };

    public ClassificationContext Context()
    {
        var ctx = new ClassificationContext { GameFolders = GameLibraryFolders.Detect(_shell.Services.Registry).ToList() };
        List<StartupEntry> startup;
        var d = _shell.Diagnostic;
        if (d != null)
        {
            lock (d) startup = d.Startup.ToList();
            foreach (var e in startup.Where(e => !string.IsNullOrEmpty(e.FilePath)))
                ctx.Startup[ProcessClassifier.Normalize(Path.GetFileName(e.FilePath!))] = (e.Classification, e.Category);
        }
        return ctx;
    }

    /// <summary>Classified, measured and grouped list of the running processes.</summary>
    public List<ProcessVerdict> InspectProcesses(TimeSpan window, CancellationToken ct)
    {
        var activityTask = Task.Run(() => ProcessSampler.Measure(window, ct));
        var facts = ProcessClassifier.CollectFacts();
        var activity = activityTask.Result.ToDictionary(a => a.Pid);
        var ctx = Context();
        var self = Environment.ProcessId;
        return ProcessClassifier.Group(facts.Where(f => f.Pid != self && f.Pid > 4 || f.Name is "System")
            .Select(f => Classifier.Classify(f, activity.TryGetValue(f.Pid, out var a) ? a : null, ctx)));
    }

    public async Task<DetectiveReport> RunAsync(bool deep, ProfileLevel level, Action<string, double, string> progress, CancellationToken ct)
    {
        var diag = _shell.Diagnostic ?? throw new InvalidOperationException(Loc.T("quick.needDiagnostic"));
        var profile = diag.Profile;
        var r = new DetectiveReport { MachineSignature = profile.Signature };
        var latencySeconds = deep ? 60 : 20;

        // Hardware and storage health (administrator rights for SMART data, otherwise what Windows exposes).
        progress("hardware", 0, "");
        try
        {
            var storage = await Task.Run(() => _shell.Privileged.StorageWithReliability(), ct);
            if (storage.Count > 0) profile.Storage = storage;
        }
        catch (Exception ex) { Log.Warn("Detective", "SMART unavailable: " + ex.Message); }
        r.Findings.AddRange(DetectiveAnalysis.Hardware(profile));
        foreach (var f in diag.Findings.Where(f => f.Severity >= Severity.Suggestion && f.Category is "Ram" or "Bios" or "Display" or "Gpu" or "Storage" or "Cpu" or "Drivers"))
            r.Findings.Add(new DetectiveFinding
            {
                Id = "diag." + f.Id, Area = f.Category is "Storage" ? DetectiveArea.Storage : DetectiveArea.Hardware, Severity = f.Severity, Title = f.Title,
                Evidence = f.Detail, Advice = f.Recommendation ?? "", Fix = f.ActionId?.StartsWith("nav:") == true ? DetectiveFix.OpenPage : DetectiveFix.None, FixTarget = f.ActionId?.Replace("nav:", ""),
            });
        progress("hardware", 1, "");

        // Internal latency (kernel trace in the elevated worker) while processes and stability are read.
        progress("latency", 0, Loc.T("detective.step.latency.detail", latencySeconds));
        var latencyTask = Task.Run(() =>
        {
            try { return _shell.Privileged.LatencyCapture(latencySeconds); }
            catch (ElevationDeclinedException) { return new LatencyReport { Error = Loc.T("elevation.declined") }; }
            catch (Exception ex) { return new LatencyReport { Error = ex.Message }; }
        }, ct);
        var stabilityTask = Task.Run(() => StabilityScanner.Read(TimeSpan.FromDays(deep ? 90 : 30)), ct);

        progress("processes", 0, "");
        r.Processes = await Task.Run(() => InspectProcesses(TimeSpan.FromSeconds(deep ? 15 : 8), ct), ct);
        r.Findings.AddRange(DetectiveAnalysis.Processes(r.Processes, level));
        progress("processes", 1, Loc.T("detective.step.processes.detail", r.Processes.Sum(p => p.Instances)));

        var sw = Stopwatch.StartNew();
        while (!latencyTask.IsCompleted && !ct.IsCancellationRequested)
        {
            progress("latency", Math.Min(0.99, sw.Elapsed.TotalSeconds / latencySeconds), Loc.T("detective.step.latency.detail", latencySeconds));
            await Task.Delay(500, CancellationToken.None);
        }
        r.Latency = await latencyTask;
        r.Findings.AddRange(DetectiveAnalysis.Latency(r.Latency));
        progress("latency", 1, "");

        // Network path (after the kernel trace so that pings do not disturb it).
        progress("network", 0, "");
        var n = new NetworkReport();
        var active = profile.Network.FirstOrDefault(a => a.IsUp && a.IsPhysical && a.Gateway != null);
        n.Adapter = active?.Description; n.Wired = active is { IsWireless: false }; n.LinkMbps = active?.SpeedMbps ?? 0;
        var count = deep ? 100 : 40;
        var gw = active?.Gateway is { } g ? NetworkProbe.PingAsync(g, count, 100, ct) : Task.FromResult<PingSummary>(null!);
        var net = NetworkProbe.PingAsync("1.1.1.1", count, 100, ct);
        var bg = Task.Run(() => NetworkProbe.BackgroundKBps(TimeSpan.FromSeconds(4)), ct);
        n.Gateway = await gw; n.Internet = await net; n.BackgroundKBps = await bg;
        r.Network = n;
        r.Findings.AddRange(NetworkProbe.Analyze(n, profile.Network));
        progress("network", 1, "");

        progress("stability", 0, "");
        r.Stability = await stabilityTask;
        r.Findings.AddRange(StabilityScanner.Analyze(r.Stability, DateTime.Now));
        progress("stability", 1, "");

        // Comparison with the first investigation and with the benchmark history.
        progress("history", 0, "");
        var sys = profile.Storage.SelectMany(s => s.Volumes).FirstOrDefault(v => v.IsSystem);
        List<StartupEntry> startup; lock (diag) startup = diag.Startup.ToList();
        var background = r.Processes.Where(p => p.Tier <= ProcessTier.LowValue).ToList();
        r.Snapshot = new SystemSnapshot
        {
            RamSpeedMts = profile.Memory.ConfiguredSpeedMts, StartupEnabled = startup.Count(e => e.Enabled),
            StartupNames = startup.Where(e => e.Enabled).Select(e => e.DisplayName ?? e.Name).Distinct().ToList(),
            ProcessCount = r.Processes.Sum(p => p.Instances), BackgroundCpuPercent = background.Sum(p => p.CpuPercent),
            WakeupsPerSec = r.Processes.Sum(p => p.WakeupsPerSec), SystemFreePercent = sys?.FreePercent ?? 0,
            GpuDriver = profile.PrimaryGpu?.DriverVersion, OsBuild = profile.Os.Build,
            MaxDpcMs = r.Latency?.Measured == true ? Math.Max(r.Latency.MaxDpcMs, r.Latency.MaxIsrMs) : null,
            GatewayPingMs = n.Gateway?.Received > 0 ? n.Gateway.AvgMs : null, CpuIdleTempC = _shell.Monitoring.Last?.CpuTemp,
        };
        var benches = _shell.Services.QuickBenches.LoadAll().Where(b => b.MachineSignature == profile.Signature || string.IsNullOrEmpty(b.MachineSignature)).ToList();
        r.Findings.AddRange(DetectiveAnalysis.Degradation(Store.Baseline(), r.Snapshot, benches));
        progress("history", 1, "");

        r.Findings = r.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Area).ToList();
        r.FinishedUtc = DateTime.UtcNow;
        Store.Save(r);
        Last = r;
        return r;
    }
}

/// <summary>Applies the fixes FrameCore can make by itself (each one confirmed by the user beforehand).</summary>
public sealed class DetectiveFixer
{
    private readonly ShellViewModel _shell;
    public DetectiveFixer(ShellViewModel shell) => _shell = shell;

    public async Task<string> ApplyAsync(DetectiveFinding f, DetectiveReport report)
    {
        switch (f.Fix)
        {
            case DetectiveFix.CloseProcess:
            case DetectiveFix.LowerPriority:
            {
                var targets = report.Processes.Where(p => string.Equals(p.Name, f.FixTarget, StringComparison.OrdinalIgnoreCase) && !p.Protected && p.Tier < ProcessTier.VeryUseful).ToList();
                var done = 0;
                foreach (var pid in targets.SelectMany(t => t.Pids))
                    done += f.Fix == DetectiveFix.CloseProcess ? (ProcessControl.Close(pid) ? 1 : 0) : (_shell.GameProfile.LowerPriority(pid) ? 1 : 0);
                if (f.Fix == DetectiveFix.CloseProcess) done += await DisableStartupOf(f.FixTarget);
                return Loc.T(f.Fix == DetectiveFix.CloseProcess ? "detective.fix.closed" : "detective.fix.lowered", f.FixTarget, done);
            }
            case DetectiveFix.GameProfile:
                _shell.GameProfile.Enabled = true;
                return Loc.T("detective.fix.gameProfile");
            case DetectiveFix.CleanShaderCache:
                return Loc.T("detective.fix.shaderCache", await Task.Run(ShaderCache.Clean));
            case DetectiveFix.OpenMemoryTest:
                Dialogs.OpenUri(Path.Combine(Environment.SystemDirectory, "mdsched.exe"));
                return Loc.T("detective.fix.memoryTest");
            case DetectiveFix.OpenPage:
                if (f.FixTarget != null) _shell.Navigate(f.FixTarget);
                return "";
            default:
                return "";
        }
    }

    /// <summary>Start-up entries of the closed program are disabled too (reversible from the Restore page).</summary>
    private async Task<int> DisableStartupOf(string? name)
    {
        var d = _shell.Diagnostic;
        if (d == null || name == null) return 0;
        List<StartupEntry> entries; lock (d) entries = d.Startup.ToList();
        var controller = new StartupController(_shell.Services.Engine);
        var n = 0;
        foreach (var e in entries.Where(e => e.Enabled && e.CanToggle && !e.Protected && e.FilePath != null
                                             && string.Equals(ProcessClassifier.Normalize(Path.GetFileName(e.FilePath)), name, StringComparison.OrdinalIgnoreCase)))
            if ((await Task.Run(() => controller.SetEnabled(e, false))).Success) n++;
        return n;
    }
}

/// <summary>Closing a process: politely when it has a window, otherwise ended (never a protected one — checked by callers).</summary>
public static class ProcessControl
{
    public static bool Close(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(3000)) return true;
            p.Kill();
            return p.WaitForExit(3000);
        }
        catch (Exception ex) { Log.Info("Processes", $"Close {pid}: {ex.Message}"); return false; }
    }
}

/// <summary>Graphics drivers' shader caches: rebuilt automatically by games; a stale cache after a driver update can cause stutter.</summary>
public static class ShaderCache
{
    public static IEnumerable<string> Folders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var low = Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow");
        return new[]
        {
            Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache"), Path.Combine(low, "NVIDIA", "PerDriverVersion", "DXCache"),
            Path.Combine(local, "D3DSCache"), Path.Combine(local, "AMD", "DxCache"), Path.Combine(local, "AMD", "DxcCache"), Path.Combine(local, "AMD", "VkCache"),
            Path.Combine(local, "Intel", "ShaderCache"),
        }.Where(Directory.Exists);
    }

    /// <summary>Deletes the cache files that are not in use; returns the freed size in MB.</summary>
    public static double Clean()
    {
        long freed = 0;
        foreach (var dir in Folders())
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { var len = new FileInfo(file).Length; File.Delete(file); freed += len; } catch { /* in use by a running game: kept */ }
        return Math.Round(freed / 1048576.0, 1);
    }
}
