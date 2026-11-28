using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>Turns measurements into findings (pure functions, unit tested).</summary>
public static class DetectiveAnalysis
{
    // ------------------------------------------------------------------ internal latency
    public static List<DetectiveFinding> Latency(LatencyReport r)
    {
        var f = new List<DetectiveFinding>();
        if (!r.Measured)
        {
            f.Add(new DetectiveFinding { Id = "lat.unavailable", Area = DetectiveArea.Latency, Severity = Severity.Info, Title = Loc.T("detective.lat.unavailable"), Evidence = r.Error ?? "" });
            return f;
        }
        var worst = r.Drivers.Where(d => d.MaxMs >= 0.5).Take(4).ToList();
        if (worst.Count == 0)
        {
            f.Add(new DetectiveFinding
            {
                Id = "lat.ok", Area = DetectiveArea.Latency, Severity = Severity.Info, Title = Loc.T("detective.lat.ok"),
                Evidence = Loc.T("detective.lat.evidence", Math.Max(r.MaxDpcMs, r.MaxIsrMs).ToString("0.000"), r.Seconds.ToString("0"), (r.DpcCount + r.IsrCount).ToString("N0")),
            });
            return f;
        }
        foreach (var d in worst)
            f.Add(new DetectiveFinding
            {
                Id = "lat." + d.Driver, Area = DetectiveArea.Latency, Severity = LatencyProbe.Verdict(d.MaxMs),
                Title = Loc.T("detective.lat.driver.title", d.Driver, d.MaxMs.ToString("0.00")),
                Evidence = Loc.T("detective.lat.driver.evidence", d.Count.ToString("N0"), d.P99Ms.ToString("0.000"), d.TotalMs.ToString("0")),
                Advice = Loc.T("detective.lat.advice." + d.Component),
                Fix = d.Component == "graphics" ? DetectiveFix.CleanShaderCache : DetectiveFix.None,
            });
        return f;
    }

    // ------------------------------------------------------------------ processes
    public static List<DetectiveFinding> Processes(IReadOnlyList<ProcessVerdict> grouped, ProfileLevel level)
    {
        var f = new List<DetectiveFinding>();
        var total = grouped.Sum(v => v.Instances);
        var background = grouped.Where(v => v.Tier <= ProcessTier.LowValue).ToList();
        var bgWake = background.Sum(v => v.WakeupsPerSec);
        var allWake = grouped.Sum(v => v.WakeupsPerSec);
        f.Add(new DetectiveFinding
        {
            Id = "proc.summary", Area = DetectiveArea.Processes,
            // Severity follows the measured cost, not the number of programs.
            Severity = bgWake > 2000 || background.Sum(v => v.CpuPercent) > 3 ? Severity.Warning : bgWake > 300 || background.Count >= 15 ? Severity.Suggestion : Severity.Info,
            Title = Loc.T("detective.proc.summary.title", total, background.Sum(v => v.Instances)),
            Evidence = Loc.T("detective.proc.summary.evidence", allWake.ToString("N0"), bgWake.ToString("N0"), background.Sum(v => v.CpuPercent).ToString("0.0"), background.Sum(v => v.MemoryMb).ToString("N0")),
            Advice = Loc.T("detective.proc.summary.advice"),
        });
        foreach (var v in grouped.Where(v => v.Tier == ProcessTier.Harmful))
        {
            var suspected = v.Category == "suspicious";
            f.Add(new DetectiveFinding
            {
                Id = "proc.harm." + v.Name, Area = DetectiveArea.Processes, Severity = suspected ? Severity.Suggestion : Severity.Warning,
                Title = Loc.T(suspected ? "detective.proc.suspicious.title" : "detective.proc.harmful.title", v.Name), Evidence = v.Reason + (v.Path != null ? " — " + v.Path : ""),
                Advice = Loc.T(suspected ? "detective.proc.suspicious.advice" : "detective.proc.harmful.advice"),
                Fix = suspected ? DetectiveFix.None : DetectiveFix.CloseProcess, FixTarget = v.Name,
            });
        }
        var noisy = grouped.Where(v => !v.Protected && v.Tier <= ProcessTier.LowValue && (v.WakeupsPerSec >= 300 || v.CpuPercent >= 1)).Take(5).ToList();
        foreach (var v in noisy)
            f.Add(new DetectiveFinding
            {
                Id = "proc.noisy." + v.Name, Area = DetectiveArea.Processes, Severity = Severity.Suggestion,
                Title = Loc.T("detective.proc.noisy.title", v.Name, v.WakeupsPerSec.ToString("N0")),
                Evidence = Loc.T("detective.proc.noisy.evidence", v.Instances, v.CpuPercent.ToString("0.0"), v.MemoryMb.ToString("N0"), Loc.T("tier." + v.Tier)),
                Advice = v.Reason,
                Fix = ProcessClassifier.ActionFor(v, level) switch { ProcessAction.Close => DetectiveFix.CloseProcess, ProcessAction.LowerPriority => DetectiveFix.LowerPriority, _ => DetectiveFix.GameProfile },
                FixTarget = v.Name,
            });
        var overlays = grouped.Where(v => v.Category == "overlay").Select(v => v.Name).ToList();
        if (overlays.Count > 0)
            f.Add(new DetectiveFinding
            {
                Id = "proc.overlays", Area = DetectiveArea.Stutter, Severity = overlays.Count >= 2 ? Severity.Suggestion : Severity.Info,
                Title = Loc.T("detective.proc.overlays.title", overlays.Count), Evidence = string.Join(", ", overlays), Advice = Loc.T("detective.proc.overlays.advice"),
            });
        return f;
    }

    // ------------------------------------------------------------------ hardware & storage
    public static List<DetectiveFinding> Hardware(MachineProfile p)
    {
        var f = new List<DetectiveFinding>();
        var gpu = p.PrimaryGpu;
        if (gpu is { PcieCurrentWidth: { } cw, PcieMaxWidth: { } mw } && cw < mw && cw > 0)
            f.Add(new DetectiveFinding
            {
                Id = "hw.pcieWidth", Area = DetectiveArea.Hardware, Severity = cw <= 4 ? Severity.Critical : Severity.Warning,
                Title = Loc.T("detective.hw.pcie.title", cw, mw), Evidence = gpu.Name, Advice = Loc.T("detective.hw.pcie.advice"),
            });
        if (gpu is { ResizableBar: ResizableBarState.Disabled, IsIntegrated: false })
            f.Add(new DetectiveFinding
            {
                Id = "hw.rebar", Area = DetectiveArea.Hardware, Severity = Severity.Suggestion, Title = Loc.T("detective.hw.rebar.title"),
                Evidence = gpu.ResizableBarDetail ?? gpu.Name, Advice = Loc.T("detective.hw.rebar.advice"), Fix = DetectiveFix.OpenPage, FixTarget = "overclock",
            });
        foreach (var d in p.Storage)
        {
            foreach (var v in d.Volumes.Where(v => v.SizeBytes > 0))
                if (v.FreePercent < 15)
                    f.Add(new DetectiveFinding
                    {
                        Id = "hw.full." + v.Letter, Area = DetectiveArea.Storage, Severity = v.FreePercent < 7 ? Severity.Warning : Severity.Suggestion,
                        Title = Loc.T("detective.hw.full.title", v.Letter, (100 - v.FreePercent).ToString("0")),
                        Evidence = Loc.T("detective.hw.full.evidence", (v.FreeBytes / 1073741824.0).ToString("0.#"), d.FriendlyName), Advice = Loc.T("detective.hw.full.advice"),
                    });
            if (d.WearPercent is >= 80)
                f.Add(new DetectiveFinding { Id = "hw.wear." + d.FriendlyName, Area = DetectiveArea.Storage, Severity = d.WearPercent >= 95 ? Severity.Critical : Severity.Warning,
                    Title = Loc.T("detective.hw.wear.title", d.FriendlyName, d.WearPercent), Advice = Loc.T("detective.hw.wear.advice") });
            if (d.TemperatureC is >= 70)
                f.Add(new DetectiveFinding { Id = "hw.ssdHot." + d.FriendlyName, Area = DetectiveArea.Storage, Severity = Severity.Suggestion,
                    Title = Loc.T("detective.hw.ssdHot.title", d.FriendlyName, d.TemperatureC), Advice = Loc.T("detective.hw.ssdHot.advice") });
            if (d.Bus is { } bus && bus.Contains("SATA", StringComparison.OrdinalIgnoreCase) == false && d.IsSystemDisk && d.FriendlyName.Contains("HDD", StringComparison.OrdinalIgnoreCase))
                f.Add(new DetectiveFinding { Id = "hw.hdd", Area = DetectiveArea.Storage, Severity = Severity.Warning, Title = Loc.T("detective.hw.hdd.title"), Advice = Loc.T("detective.hw.hdd.advice") });
        }
        return f;
    }

    // ------------------------------------------------------------------ "better when it was new"
    public static List<DetectiveFinding> Degradation(SystemSnapshot? baseline, SystemSnapshot now, IReadOnlyList<QuickBenchResult> benches)
    {
        var f = new List<DetectiveFinding>();
        if (baseline != null && baseline.Utc < now.Utc)
        {
            var since = baseline.Utc.ToLocalTime().ToString("d");
            if (baseline.RamSpeedMts is { } b && now.RamSpeedMts is { } n && n < b - 100)
                f.Add(new DetectiveFinding { Id = "deg.ram", Area = DetectiveArea.Degradation, Severity = Severity.Warning,
                    Title = Loc.T("detective.deg.ram.title", b, n), Evidence = Loc.T("detective.deg.since", since), Advice = Loc.T("detective.deg.ram.advice"),
                    Fix = DetectiveFix.OpenPage, FixTarget = "overclock" });
            var newStartup = now.StartupNames.Except(baseline.StartupNames, StringComparer.OrdinalIgnoreCase).ToList();
            if (newStartup.Count >= 2)
                f.Add(new DetectiveFinding { Id = "deg.startup", Area = DetectiveArea.Degradation, Severity = Severity.Suggestion,
                    Title = Loc.T("detective.deg.startup.title", newStartup.Count), Evidence = string.Join(", ", newStartup.Take(8)), Advice = Loc.T("detective.deg.startup.advice") });
            if (now.WakeupsPerSec > baseline.WakeupsPerSec * 1.3 && now.WakeupsPerSec - baseline.WakeupsPerSec > 1500)
                f.Add(new DetectiveFinding { Id = "deg.wakeups", Area = DetectiveArea.Degradation, Severity = Severity.Suggestion,
                    Title = Loc.T("detective.deg.wakeups.title", ((now.WakeupsPerSec / Math.Max(1, baseline.WakeupsPerSec) - 1) * 100).ToString("0")),
                    Evidence = Loc.T("detective.deg.wakeups.evidence", baseline.WakeupsPerSec.ToString("N0"), now.WakeupsPerSec.ToString("N0"), since) });
            if (baseline.SystemFreePercent - now.SystemFreePercent > 15)
                f.Add(new DetectiveFinding { Id = "deg.disk", Area = DetectiveArea.Degradation, Severity = Severity.Suggestion,
                    Title = Loc.T("detective.deg.disk.title", baseline.SystemFreePercent.ToString("0"), now.SystemFreePercent.ToString("0")), Advice = Loc.T("detective.hw.full.advice") });
            if (baseline.GpuDriver != null && now.GpuDriver != null && baseline.GpuDriver != now.GpuDriver)
                f.Add(new DetectiveFinding { Id = "deg.driver", Area = DetectiveArea.Degradation, Severity = Severity.Info,
                    Title = Loc.T("detective.deg.driver.title", baseline.GpuDriver, now.GpuDriver), Advice = Loc.T("detective.deg.driver.advice"), Fix = DetectiveFix.CleanShaderCache });
            if (baseline.MaxDpcMs is { } bd && now.MaxDpcMs is { } nd && nd > Math.Max(0.5, bd * 2))
                f.Add(new DetectiveFinding { Id = "deg.latency", Area = DetectiveArea.Degradation, Severity = Severity.Warning,
                    Title = Loc.T("detective.deg.latency.title", bd.ToString("0.00"), nd.ToString("0.00")), Evidence = Loc.T("detective.deg.since", since) });
        }
        // Benchmarks: first comparable run vs the latest one, same mode.
        var latest = benches.OrderByDescending(b => b.StartedUtc).FirstOrDefault(b => !b.Cancelled);
        var first = latest == null ? null : benches.Where(b => b.Mode == latest.Mode && !b.Cancelled && b.Id != latest.Id && b.StartedUtc < latest.StartedUtc.AddDays(-1))
            .OrderBy(b => b.StartedUtc).FirstOrDefault();
        if (latest != null && first != null)
        {
            var cmp = QuickBenchComparer.Compare(first, latest);
            foreach (var m in cmp.Metrics.Where(m => m.Verdict == ChangeVerdict.Regressed && m.ImprovementPercent is <= -5).OrderBy(m => m.ImprovementPercent).Take(4))
                f.Add(new DetectiveFinding { Id = "deg.bench." + m.Key, Area = DetectiveArea.Degradation, Severity = m.ImprovementPercent <= -15 ? Severity.Warning : Severity.Suggestion,
                    Title = Loc.T("detective.deg.bench.title", Loc.T("metric." + m.Key), m.ImprovementPercent!.Value.ToString("0")),
                    Evidence = Loc.T("detective.deg.bench.evidence", first.StartedUtc.ToLocalTime().ToString("d"), latest.StartedUtc.ToLocalTime().ToString("d")),
                    Advice = Loc.T(m.Component switch { BenchComponent.Cpu => "detective.deg.bench.cpu", BenchComponent.Storage => "detective.deg.bench.storage", BenchComponent.Memory => "detective.deg.ram.advice", _ => "detective.deg.bench.other" }) });
            if (latest.Thermals.CpuTempMaxC is { } tNow && first.Thermals.CpuTempMaxC is { } tThen && tNow - tThen >= 8)
                f.Add(new DetectiveFinding { Id = "deg.temp", Area = DetectiveArea.Degradation, Severity = tNow >= 90 ? Severity.Warning : Severity.Suggestion,
                    Title = Loc.T("detective.deg.temp.title", (tNow - tThen).ToString("0"), tNow.ToString("0")),
                    Evidence = Loc.T("detective.deg.bench.evidence", first.StartedUtc.ToLocalTime().ToString("d"), latest.StartedUtc.ToLocalTime().ToString("d")),
                    Advice = Loc.T("detective.deg.temp.advice") });
        }
        if (baseline == null)
            f.Add(new DetectiveFinding { Id = "deg.baseline", Area = DetectiveArea.Degradation, Severity = Severity.Info, Title = Loc.T("detective.deg.baselineSaved") });
        else if (f.Count == 0)
            f.Add(new DetectiveFinding { Id = "deg.none", Area = DetectiveArea.Degradation, Severity = Severity.Info, Title = Loc.T("detective.deg.none", baseline.Utc.ToLocalTime().ToString("d")) });
        return f;
    }
}

/// <summary>Investigations saved on this PC; the first one is the baseline later ones are compared with.</summary>
public sealed class DetectiveStore
{
    private readonly string _dir;
    public DetectiveStore(string? dir = null) { _dir = dir ?? Path.Combine(AppPaths.Root, "Detective"); }

    public void Save(DetectiveReport r)
    {
        Directory.CreateDirectory(_dir);
        Json.WriteAtomic(Path.Combine(_dir, $"report-{r.StartedUtc:yyyyMMddHHmmss}.json"), r);
        var baseline = Path.Combine(_dir, "baseline.json");
        if (!File.Exists(baseline)) Json.WriteAtomic(baseline, r.Snapshot);
    }

    public SystemSnapshot? Baseline()
    {
        try { var p = Path.Combine(_dir, "baseline.json"); return File.Exists(p) ? Json.Read<SystemSnapshot>(p) : null; }
        catch { return null; }
    }

    public DetectiveReport? Latest()
    {
        try
        {
            if (!Directory.Exists(_dir)) return null;
            var f = Directory.GetFiles(_dir, "report-*.json").OrderByDescending(x => x).FirstOrDefault();
            return f == null ? null : Json.Read<DetectiveReport>(f);
        }
        catch { return null; }
    }
}
