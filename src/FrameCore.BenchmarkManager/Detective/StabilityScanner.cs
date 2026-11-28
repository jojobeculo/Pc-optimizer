using System.Diagnostics.Eventing.Reader;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>
/// Crashes and hardware errors recorded by Windows: blue screens (with their stop code), unexpected restarts, corrected
/// hardware errors (WHEA — typical of an unstable overclock, EXPO or undervolt), graphics driver resets, game crashes
/// and disk errors. Each kind is turned into a probable cause.
/// </summary>
public static class StabilityScanner
{
    public static List<StabilityEvent> Read(TimeSpan window)
    {
        var list = new List<StabilityEvent>();
        if (!OperatingSystem.IsWindows()) return list;
        var ms = (long)window.TotalMilliseconds;
        string Q(string providers) => $"*[System[({providers}) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        void Read(string log, string query, Func<EventRecord, StabilityEvent?> map, int max = 200)
        {
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, query) { ReverseDirection = true });
                for (var i = 0; i < max; i++)
                {
                    using var e = reader.ReadEvent();
                    if (e == null) break;
                    try { if (map(e) is { } s) list.Add(s); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("Stability", $"{log}: {ex.Message}"); }
        }
        DateTime T(EventRecord e) => e.TimeCreated ?? DateTime.MinValue;
        string? Prop(EventRecord e, int i) => e.Properties.Count > i ? e.Properties[i].Value?.ToString() : null;

        Read("System", Q("Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001"),
            e => new StabilityEvent { Time = T(e), Kind = "bugcheck", Code = BugcheckCode(Prop(e, 0)), Detail = Prop(e, 0) ?? "" });
        Read("System", Q("Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41"),
            e => new StabilityEvent { Time = T(e), Kind = "unexpectedShutdown", Code = Prop(e, 0) is { } c && c != "0" ? "0x" + Convert.ToUInt64(c).ToString("X") : null });
        Read("System", Q("Provider[@Name='Microsoft-Windows-WHEA-Logger']"),
            e => new StabilityEvent { Time = T(e), Kind = "whea", Code = e.Id.ToString(), Detail = SafeDescription(e) });
        Read("System", Q("Provider[@Name='Display'] and EventID=4101"), e => new StabilityEvent { Time = T(e), Kind = "gpuReset" });
        Read("System", Q("(Provider[@Name='disk'] and (EventID=7 or EventID=51 or EventID=153)) or (Provider[@Name='Ntfs'] and (EventID=55 or EventID=98)) or (Provider[@Name='stornvme'] and (EventID=11 or EventID=129))"),
            e => new StabilityEvent { Time = T(e), Kind = "diskError", Code = $"{e.ProviderName}/{e.Id}" });
        Read("System", Q("Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']"),
            e => new StabilityEvent { Time = T(e), Kind = "memoryTest", Code = e.Id.ToString(), Detail = SafeDescription(e) }, 5);
        Read("Application", Q("Provider[@Name='Application Error'] and EventID=1000"),
            e => new StabilityEvent { Time = T(e), Kind = "appCrash", Detail = Prop(e, 0) ?? "", Code = Prop(e, 3) });
        return list.OrderByDescending(x => x.Time).ToList();
    }

    private static string SafeDescription(EventRecord e)
    {
        try { var d = e.FormatDescription() ?? ""; return d.Length > 300 ? d[..300] : d; } catch { return ""; }
    }

    /// <summary>"0x0000001a (0x…)" or "0x1a" → "0x1A".</summary>
    public static string? BugcheckCode(string? text)
    {
        if (text == null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"0x([0-9a-fA-F]{1,16})");
        return m.Success ? "0x" + Convert.ToUInt64(m.Groups[1].Value, 16).ToString("X") : null;
    }

    /// <summary>Probable cause of a stop code (the most frequent ones on gaming PCs).</summary>
    public static string CauseKey(string? code) => code switch
    {
        "0x124" => "whea",            // WHEA_UNCORRECTABLE_ERROR: unstable CPU/RAM overclock, voltage, overheating
        "0x101" => "cpuOc",           // CLOCK_WATCHDOG_TIMEOUT: CPU overclock/undervolt (Curve Optimizer) too far
        "0x1A" or "0x50" or "0x4E" or "0x139" => "memory", // MEMORY_MANAGEMENT, PAGE_FAULT…, PFN_LIST_CORRUPT, KERNEL_SECURITY_CHECK
        "0x116" or "0x117" or "0x119" or "0x10E" => "gpu", // VIDEO_TDR_FAILURE…
        "0x133" or "0x9F" or "0xD1" or "0xA" or "0x3B" or "0x7E" or "0x1E" => "driver",
        "0x7A" or "0x77" or "0xF4" or "0xEF" => "storage",
        _ => "unknown",
    };

    /// <summary>Findings from the events (pure, tested).</summary>
    public static List<DetectiveFinding> Analyze(IReadOnlyList<StabilityEvent> events, DateTime now)
    {
        var f = new List<DetectiveFinding>();
        var bsod = events.Where(e => e.Kind == "bugcheck").ToList();
        if (bsod.Count > 0)
        {
            var main = bsod.GroupBy(e => CauseKey(e.Code)).OrderByDescending(g => g.Count()).First();
            f.Add(new DetectiveFinding
            {
                Id = "stab.bsod", Area = DetectiveArea.Stability, Severity = bsod.Count >= 2 ? Severity.Critical : Severity.Warning,
                Title = Loc.T("detective.stab.bsod.title", bsod.Count),
                Evidence = Loc.T("detective.stab.bsod.evidence", string.Join(", ", bsod.Select(b => b.Code ?? "?").Distinct().Take(5)), bsod[0].Time.ToString("g")),
                Advice = Loc.T("detective.stab.cause." + main.Key),
                Fix = main.Key == "memory" ? DetectiveFix.OpenMemoryTest : DetectiveFix.None,
            });
        }
        var shutdowns = events.Count(e => e.Kind == "unexpectedShutdown");
        if (shutdowns > bsod.Count)
            f.Add(new DetectiveFinding
            {
                Id = "stab.shutdown", Area = DetectiveArea.Stability, Severity = shutdowns - bsod.Count >= 2 ? Severity.Warning : Severity.Suggestion,
                Title = Loc.T("detective.stab.shutdown.title", shutdowns - bsod.Count), Evidence = Loc.T("detective.stab.shutdown.evidence"),
                Advice = Loc.T("detective.stab.shutdown.advice"),
            });
        var whea = events.Where(e => e.Kind == "whea").ToList();
        if (whea.Count > 0)
            f.Add(new DetectiveFinding
            {
                Id = "stab.whea", Area = DetectiveArea.Stability, Severity = whea.Count >= 5 ? Severity.Critical : Severity.Warning,
                Title = Loc.T("detective.stab.whea.title", whea.Count),
                Evidence = Loc.T("detective.stab.whea.evidence", string.Join(", ", whea.Select(w => w.Code).Distinct())),
                Advice = Loc.T(whea.Any(w => w.Code == "19") ? "detective.stab.whea.cpu" : whea.Any(w => w.Code == "17") ? "detective.stab.whea.pcie" : "detective.stab.whea.generic"),
            });
        var tdr = events.Count(e => e.Kind == "gpuReset");
        if (tdr > 0)
            f.Add(new DetectiveFinding
            {
                Id = "stab.tdr", Area = DetectiveArea.Stability, Severity = tdr >= 3 ? Severity.Warning : Severity.Suggestion,
                Title = Loc.T("detective.stab.tdr.title", tdr), Evidence = Loc.T("detective.stab.tdr.evidence"), Advice = Loc.T("detective.stab.tdr.advice"),
            });
        var crashes = events.Where(e => e.Kind == "appCrash").GroupBy(e => e.Detail, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count()).Take(3).ToList();
        foreach (var g in crashes)
            f.Add(new DetectiveFinding
            {
                Id = "stab.app." + g.Key, Area = DetectiveArea.Stability, Severity = Severity.Suggestion,
                Title = Loc.T("detective.stab.app.title", g.Key, g.Count()), Evidence = Loc.T("detective.stab.app.evidence", g.Select(x => x.Code).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "?"),
                Advice = Loc.T("detective.stab.app.advice"),
            });
        var disk = events.Count(e => e.Kind == "diskError");
        if (disk > 0)
            f.Add(new DetectiveFinding
            {
                Id = "stab.disk", Area = DetectiveArea.Storage, Severity = disk >= 5 ? Severity.Critical : Severity.Warning,
                Title = Loc.T("detective.stab.disk.title", disk), Evidence = string.Join(", ", events.Where(e => e.Kind == "diskError").Select(e => e.Code).Distinct().Take(4)),
                Advice = Loc.T("detective.stab.disk.advice"),
            });
        if (f.Count == 0)
            f.Add(new DetectiveFinding { Id = "stab.ok", Area = DetectiveArea.Stability, Severity = Severity.Info, Title = Loc.T("detective.stab.ok"), Evidence = Loc.T("detective.stab.window") });
        return f;
    }
}
