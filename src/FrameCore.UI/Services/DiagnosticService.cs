using System.Globalization;
using FrameCore.BiosManager;
using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Engine;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.CpuManager;
using FrameCore.DriverManager;
using FrameCore.HardwareDetection;
using FrameCore.StartupManager;
using FrameCore.WindowsOptimization.Platform;

namespace FrameCore.UI.Services;

public sealed class DiagnosticResult
{
    public MachineProfile Profile { get; set; } = new();
    public MachineFacts Facts { get; set; } = new();
    public List<TweakEvaluation> Evaluations { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
    public List<DriverEntry> Drivers { get; set; } = new();
    public List<StartupEntry> Startup { get; set; } = new();
    /// <summary>The startup analysis is slow on some PCs: it completes after the diagnostic is shown.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Task StartupTask { get; set; } = Task.CompletedTask;
    public CpuTuningReport? CpuTuning { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public int Count(Severity s) => Findings.Count(f => f.Severity == s);
}

/// <summary>Runs the full diagnosis and turns raw data into explained findings.</summary>
public sealed class DiagnosticService
{
    private readonly AppServices _s;
    public DiagnosticResult? Last { get; private set; }
    public event EventHandler<DiagnosticResult>? Completed;

    public DiagnosticService(AppServices services) => _s = services;

    public Task<DiagnosticResult> RunAsync(IProgress<string>? progress = null) => Task.Run(() =>
    {
        progress = TimedProgress(progress);
        var r = new DiagnosticResult();
        r.Profile = _s.CreateScanner().Scan(progress);
        progress?.Report(Loc.T("scan.facts"));
        r.Facts = BuildFacts(r.Profile);
        // Catalog evaluation, driver inventory and startup analysis are independent: run them side by side.
        progress?.Report(Loc.T("scan.parallel"));
        var drivers = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { r.Drivers = DriverInventory.Scan(r.Profile); } catch (Exception ex) { Log.Error("Diag", "drivers", ex); }
            Log.Info("Diag", $"{sw.ElapsedMilliseconds,6} ms  drivers (parallel)");
        });
        // The startup scan does not block the diagnostic: its findings are added when it completes.
        r.StartupTask = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<StartupEntry> list = new();
            try { list = _s.CreateStartupScanner().ScanAll(includeServices: true, includeShellExtensions: false); } catch (Exception ex) { Log.Error("Diag", "startup", ex); }
            Log.Info("Diag", $"{sw.ElapsedMilliseconds,6} ms  startup (background)");
            lock (r) r.Startup = list; // findings are added on the UI thread (see AddStartupFindings)
        });
        r.Evaluations = EvaluateAll(r.Facts);
        drivers.Wait();
        progress?.Report(Loc.T("scan.findings"));
        r.CpuTuning = CpuTuningAdvisor.Analyze(r.Profile, _s.Registry);
        r.Findings = BuildFindings(r);
        r.CompletedAt = DateTime.Now;
        Last = r;
        progress?.Report("done");
        Log.Info("Diag", $"Diagnostic completed: {r.Findings.Count} findings, {r.Evaluations.Count} catalog entries evaluated");
        Completed?.Invoke(this, r);
        return r;
    });

    /// <summary>Logs how long each diagnostic phase took (performance tracking).</summary>
    private static IProgress<string> TimedProgress(IProgress<string>? inner)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? phase = null;
        return new SyncProgress(m =>
        {
            if (phase != null) Log.Info("Diag", $"{sw.ElapsedMilliseconds,6} ms  {phase}");
            phase = m;
            sw.Restart();
            if (m != "done") inner?.Report(m);
        });
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    public MachineFacts BuildFacts(MachineProfile p)
    {
        IReadOnlyCollection<Guid>? schemes = null;
        try { schemes = _s.Power.ListSchemes().Select(x => x.Guid).ToList(); } catch { }
        return MachineFactsBuilder.Build(p, schemes, MachineFactsBuilder.CountPhysicalPrinters());
    }

    public List<TweakEvaluation> EvaluateAll(MachineFacts facts) =>
        _s.Catalog.Tweaks.Select(t =>
        {
            try { return _s.Engine.Evaluate(t, facts); }
            catch (Exception ex) { return new TweakEvaluation { Tweak = t, Status = TweakStatus.Error, ActionStates = new[] { ActionState.Unreadable(ex.Message) } }; }
        }).ToList();

    /// <summary>Regenerates the findings texts (e.g. after a language change) without scanning again.</summary>
    public void RebuildFindings(DiagnosticResult r) => r.Findings = BuildFindings(r);

    /// <summary>Called on the UI thread once the background startup scan has completed.</summary>
    public static void AddStartupFindings(DiagnosticResult r)
    {
        foreach (var f in StartupFindings(r)) if (r.Findings.All(e => e.Id != f.Id)) r.Findings.Add(f);
    }

    private static IEnumerable<Finding> StartupFindings(DiagnosticResult r)
    {
        var optionalOn = r.Startup.Count(e => e.Enabled && e.Kind is StartupKind.RunKey or StartupKind.StartupFolder or StartupKind.ScheduledTask && e.Classification is StartupClassification.Optional or StartupClassification.Risky);
        if (optionalOn >= 4)
            yield return new Finding { Id = "startup.many", Category = "Startup", Severity = Severity.Suggestion, Title = Loc.T("find.startup.title"), Detail = Loc.T("find.startup.detail", optionalOn), ActionId = "nav:pro:startup", ActionLabel = Loc.T("action.review") };
        foreach (var e in r.Startup.Where(e => e.Classification == StartupClassification.Risky && e.Enabled))
            yield return new Finding { Id = "startup.risky." + e.Id, Category = "Startup", Severity = Severity.Warning, Confidence = Confidence.Probable, Title = Loc.T("find.startup.risky.title", e.Name), Detail = e.Reason ?? "", ActionId = "nav:pro:startup" };
    }

    private List<Finding> BuildFindings(DiagnosticResult r)
    {
        var p = r.Profile;
        var f = new List<Finding>();
        void Add(Finding x) { if (f.All(e => e.Id != x.Id)) f.Add(x); }

        // ---- Windows
        if (!p.Os.IsWindows11)
            Add(new Finding { Id = "os.notWin11", Category = "Windows", Severity = Severity.Warning, Title = Loc.T("find.os.notWin11.title"), Detail = Loc.T("find.os.notWin11.detail", p.Os.ProductName, p.Os.FullBuild) });
        if (p.Os.ServicingEnded == true)
            Add(new Finding { Id = "os.servicing", Category = "Windows", Severity = Severity.Critical, Title = Loc.T("find.os.servicing.title"), Detail = p.Os.ServicingNote ?? "", Recommendation = Loc.T("find.os.servicing.reco"), ActionId = "open:ms-settings:windowsupdate" });
        if (p.Os.PendingReboot)
            Add(new Finding { Id = "os.pendingReboot", Category = "Windows", Severity = Severity.Info, Title = Loc.T("find.os.reboot.title"), Detail = Loc.T("find.os.reboot.detail") });

        // ---- Repairs available from the catalog (bad settings left by other tools)
        foreach (var ev in r.Evaluations.Where(e => e.Compatible && e.Tweak.Kind == TweakKind.Repair && e.Status == TweakStatus.NotApplied && e.Tweak.Reversible))
            Add(new Finding
            {
                Id = "repair." + ev.Tweak.Id, Category = ev.Tweak.Category, Severity = Severity.Warning, Confidence = ev.Tweak.Confidence,
                Title = ev.Tweak.Name.ToString(), Detail = Loc.T("find.repair.detail", ev.CurrentSummary, ev.TargetSummary),
                Recommendation = ev.Tweak.Impact.ToString(), RelatedTweakIds = { ev.Tweak.Id }, ActionId = "tweak:" + ev.Tweak.Id, ActionLabel = Loc.T("action.repair"),
            });
        var gameMode = r.Evaluations.FirstOrDefault(e => e.Tweak.Id == "gaming.gamemode");
        if (gameMode is { Status: TweakStatus.NotApplied or TweakStatus.Partial })
            Add(new Finding { Id = "gamemode.off", Category = "Windows", Severity = Severity.Suggestion, Title = Loc.T("find.gamemode.title"), Detail = Loc.T("find.gamemode.detail"), ActionId = "tweak:gaming.gamemode", ActionLabel = Loc.T("action.enable") });

        // ---- Displays
        foreach (var d in p.Displays)
        {
            if (d.MaxRefreshAtCurrentResolution is { } max && max > d.CurrentRefreshHz + 1.5)
                Add(new Finding
                {
                    Id = "display.refresh." + d.DeviceName, Category = "Display", Severity = Severity.Warning,
                    Title = Loc.T("find.display.refresh.title", d.FriendlyName ?? d.DeviceName),
                    Detail = Loc.T("find.display.refresh.detail", d.CurrentRefreshHz.ToString("0.##", CultureInfo.CurrentCulture), max.ToString("0", CultureInfo.CurrentCulture), d.CurrentWidth, d.CurrentHeight),
                    ActionId = "tweak:display.maxRefresh", ActionLabel = Loc.T("action.setMaxRefresh"), RelatedTweakIds = { "display.maxRefresh" },
                });
            if (d.LinkLimitNote != null)
                Add(new Finding { Id = "display.link." + d.DeviceName, Category = "Display", Severity = Severity.Info, Confidence = Confidence.Probable, Title = Loc.T("find.display.link.title"), Detail = d.LinkLimitNote });
            if (d.NativeWidth is { } nw && d.NativeHeight is { } nh && (nw != d.CurrentWidth || nh != d.CurrentHeight) && !d.IsInternal)
                Add(new Finding { Id = "display.native." + d.DeviceName, Category = "Display", Severity = Severity.Info, Title = Loc.T("find.display.native.title"), Detail = Loc.T("find.display.native.detail", d.CurrentWidth, d.CurrentHeight, nw, nh) });
        }

        // ---- Memory
        var mem = p.Memory;
        var totalGb = mem.TotalBytes / 1073741824.0;
        if (mem.Channels == ChannelMode.Single && !p.Board.IsLaptop)
            Add(new Finding { Id = "ram.single", Category = "Ram", Severity = Severity.Warning, Confidence = Confidence.Probable, Title = Loc.T("find.ram.single.title"), Detail = (mem.ChannelReasoning ?? "") + " " + Loc.T("find.ram.single.detail"), Recommendation = Loc.T("find.ram.single.reco") });
        if (totalGb > 0 && totalGb < 15)
            Add(new Finding { Id = "ram.low", Category = "Ram", Severity = Severity.Suggestion, Title = Loc.T("find.ram.low.title"), Detail = Loc.T("find.ram.low.detail", totalGb.ToString("0.#", CultureInfo.CurrentCulture)) });
        if (mem.Modules.Select(m => m.CapacityBytes).Distinct().Count() > 1 || mem.Modules.Select(m => m.PartNumber).Distinct().Count() > 1)
            Add(new Finding { Id = "ram.mixed", Category = "Ram", Severity = Severity.Info, Confidence = Confidence.Probable, Title = Loc.T("find.ram.mixed.title"), Detail = Loc.T("find.ram.mixed.detail") });

        // ---- Storage
        foreach (var dev in p.Storage)
        {
            if (dev.Health is "Warning" or "Unhealthy")
                Add(new Finding { Id = "storage.health." + dev.FriendlyName, Category = "Storage", Severity = Severity.Critical, Title = Loc.T("find.storage.health.title", dev.FriendlyName), Detail = Loc.T("find.storage.health.detail", dev.Health), Recommendation = Loc.T("find.storage.health.reco") });
            if (dev.IsSystemDisk && dev.Media == MediaKind.Hdd)
                Add(new Finding { Id = "storage.hddSystem", Category = "Storage", Severity = Severity.Suggestion, Title = Loc.T("find.storage.hdd.title"), Detail = Loc.T("find.storage.hdd.detail") });
            if (dev.TemperatureC is > 70)
                Add(new Finding { Id = "storage.hot." + dev.FriendlyName, Category = "Storage", Severity = Severity.Warning, Title = Loc.T("find.storage.hot.title", dev.FriendlyName), Detail = Loc.T("find.storage.hot.detail", dev.TemperatureC) });
            foreach (var v in dev.Volumes)
            {
                if (v.FreePercent < 10 || v.FreeBytes < 20L * 1073741824 && v.IsSystem)
                    Add(new Finding { Id = "storage.space." + v.Letter, Category = "Storage", Severity = v.IsSystem ? Severity.Warning : Severity.Suggestion, Title = Loc.T("find.storage.space.title", v.Letter), Detail = Loc.T("find.storage.space.detail", v.FreePercent.ToString("0", CultureInfo.CurrentCulture), v.FreeBytes / 1073741824), ActionId = "tweak:cleanup.temp.user", ActionLabel = Loc.T("action.cleanup") });
            }
        }

        // ---- Power
        try
        {
            var active = _s.Power.GetActiveScheme();
            if (active == PowerGuids.PowerSaver && !p.Board.IsLaptop)
                Add(new Finding { Id = "power.saver", Category = "Cpu", Severity = Severity.Warning, Title = Loc.T("find.power.saver.title"), Detail = Loc.T("find.power.saver.detail") });
            if (p.Cpu.IsDualCcdX3D && active != PowerGuids.Balanced)
                Add(new Finding { Id = "x3d.plan", Category = "Cpu", Severity = Severity.Warning, Confidence = Confidence.Probable, Title = Loc.T("find.x3d.plan.title"), Detail = Loc.T("find.x3d.plan.detail", PowerGuids.FriendlyScheme(active)), ActionId = "open:ms-settings:powersleep" });
        }
        catch { }
        if (p.Cpu.IsDualCcdX3D)
        {
            var gameBar = _s.Registry.KeyExists(@"HKCU\Software\Microsoft\GameBar");
            Add(new Finding
            {
                Id = "x3d.checklist", Category = "Cpu", Severity = Severity.Info, Confidence = Confidence.Probable, Title = Loc.T("find.x3d.title"),
                Detail = Loc.T("find.x3d.detail", p.InstalledVendorTools.Contains("AMD Chipset Software") ? Loc.T("common.yes") : Loc.T("common.no"), gameBar ? Loc.T("common.yes") : Loc.Unknown),
            });
        }

        // ---- GPU
        foreach (var g in p.Gpus.Where(g => !g.IsSoftware && !g.IsIntegrated))
        {
            if (g.PcieCurrentWidth is { } w && g.PcieMaxWidth is { } mw && w < mw)
                Add(new Finding { Id = "gpu.pcieWidth." + g.Name, Category = "Gpu", Severity = Severity.Warning, Confidence = Confidence.Probable, Title = Loc.T("find.gpu.pcie.title", g.Name), Detail = Loc.T("find.gpu.pcie.detail", w, mw) });
            if (g.HagsSupported == true && g.HagsEnabled == false)
                Add(new Finding { Id = "gpu.hags", Category = "Gpu", Severity = Severity.Info, Confidence = Confidence.Probable, Title = Loc.T("find.gpu.hags.title"), Detail = Loc.T("find.gpu.hags.detail"), ActionId = "tweak:graphics.hags", ActionLabel = Loc.T("action.enable") });
        }
        if (p.Board.IsLaptop && p.Gpus.Any(g => g.IsIntegrated) && p.Gpus.Any(g => !g.IsIntegrated && !g.IsSoftware))
            Add(new Finding { Id = "gpu.hybrid", Category = "Gpu", Severity = Severity.Info, Title = Loc.T("find.gpu.hybrid.title"), Detail = Loc.T("find.gpu.hybrid.detail"), ActionId = "open:ms-settings:display-advancedgraphics" });

        // ---- Security (analysis only; never changed automatically)
        if (p.Security.HvciRunning == TriState.Yes)
            Add(new Finding
            {
                Id = "security.hvci", Category = "Security", Severity = Severity.Info, Confidence = p.Security.MbecAvailable == TriState.No ? Confidence.Probable : Confidence.Experimental,
                Title = Loc.T("find.hvci.title"), Detail = p.Security.MbecAvailable == TriState.No ? Loc.T("find.hvci.noMbec") : Loc.T("find.hvci.detail"),
                Recommendation = Loc.T("find.hvci.reco"), ActionId = "open:windowsdefender://coreisolation", ActionLabel = Loc.T("action.openSecurity"),
                Links = { new LinkInfo("Microsoft", "https://support.microsoft.com/en-us/windows/options-to-optimize-gaming-performance-in-windows-11-a255f612-2949-4373-a566-ff6f3f474613") },
            });
        if (p.Security.DefenderRealTime == TriState.No)
            Add(new Finding { Id = "security.defender", Category = "Security", Severity = Severity.Warning, Title = Loc.T("find.defender.title"), Detail = Loc.T("find.defender.detail"), ActionId = "open:windowsdefender:" });
        if (p.Security.FirewallEnabled == TriState.No)
            Add(new Finding { Id = "security.firewall", Category = "Security", Severity = Severity.Warning, Title = Loc.T("find.firewall.title"), Detail = Loc.T("find.firewall.detail") });

        // ---- Startup (usually added later, when the background scan completes)
        foreach (var x in StartupFindings(r)) Add(x);

        // ---- Drivers, firmware, CPU vendor specifics
        try { foreach (var x in DriverInventory.Analyze(r.Drivers, p)) Add(x); } catch { }
        try { foreach (var x in FirmwareAdvisor.Analyze(p, DriverManager.BoardSupport.SupportUrl(p.Board))) Add(x); } catch { }
        if (r.CpuTuning != null) foreach (var x in r.CpuTuning.Findings) Add(x);

        return f.OrderByDescending(x => x.Severity).ThenBy(x => x.Category).ToList();
    }
}
