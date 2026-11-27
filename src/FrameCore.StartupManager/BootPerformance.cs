using System.Globalization;
using System.Xml.Linq;
using FrameCore.Core;

namespace FrameCore.StartupManager;

/// <summary>One full Windows start measured by Windows itself (Diagnostics-Performance event 100).</summary>
public sealed class BootRecord
{
    public DateTime StartUtc { get; set; }
    /// <summary>From power-on hand-over to the system being idle after the session opened.</summary>
    public double TotalSeconds { get; set; }
    /// <summary>Until the desktop is shown ("main path").</summary>
    public double MainPathSeconds { get; set; }
    /// <summary>After the desktop is shown, until background start-up activity has settled.</summary>
    public double PostBootSeconds { get; set; }
    /// <summary>Time Windows reports waiting at the sign-in screen (password, PIN), when it reports it.</summary>
    public double? SignInWaitSeconds { get; set; }
    public int? StartupApps { get; set; }
    public bool AfterUpdate { get; set; }
    /// <summary>Windows itself flagged this start as slower than usual.</summary>
    public bool FlaggedSlow { get; set; }
}

public enum BootCulpritKind { Application, Driver, Service, Device, Other }

/// <summary>Something Windows says made a start slower (Diagnostics-Performance events 101 to 110).</summary>
public sealed class BootCulprit
{
    public DateTime TimeUtc { get; set; }
    public int EventId { get; set; }
    public BootCulpritKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? FriendlyName { get; set; }
    public double TotalSeconds { get; set; }
    public double DelaySeconds { get; set; }
    public string Display => string.IsNullOrWhiteSpace(FriendlyName) ? Name : FriendlyName!;
}

public sealed class BootCulpritSummary
{
    public BootCulpritKind Kind { get; set; }
    public string Name { get; set; } = "";
    public int Occurrences { get; set; }
    public double AverageDelaySeconds { get; set; }
    public double MaxDelaySeconds { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

/// <summary>Result of reading the boot history. Serializable: it also comes back from the elevated worker.</summary>
public sealed class BootHistory
{
    public List<BootRecord> Boots { get; set; } = new();
    public List<BootCulprit> Culprits { get; set; } = new();
    /// <summary>The log exists but reading it needs administrator rights on this PC.</summary>
    public bool AccessDenied { get; set; }
    /// <summary>The log is absent or disabled (some editions, virtual machines).</summary>
    public bool Unavailable { get; set; }
    public string? Error { get; set; }

    public BootRecord? Last => Boots.OrderByDescending(b => b.StartUtc).FirstOrDefault();

    /// <summary>Median of the last <paramref name="n"/> starts (until the desktop), to smooth a single slow start.</summary>
    public double? RecentMedianMainPath(int n = 5)
    {
        var v = Boots.OrderByDescending(b => b.StartUtc).Take(n).Select(b => b.MainPathSeconds).OrderBy(x => x).ToList();
        return v.Count == 0 ? null : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>Recent median compared with the median of the starts before it (positive = slower now).</summary>
    public double? TrendSeconds(int n = 5)
    {
        var ordered = Boots.OrderByDescending(b => b.StartUtc).ToList();
        if (ordered.Count < n * 2) return null;
        static double Median(IEnumerable<double> x) { var s = x.OrderBy(v => v).ToList(); return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2; }
        return Median(ordered.Take(n).Select(b => b.MainPathSeconds)) - Median(ordered.Skip(n).Take(n).Select(b => b.MainPathSeconds));
    }

    /// <summary>What slowed the starts down most, grouped by component, worst first.</summary>
    public List<BootCulpritSummary> TopCulprits(int max = 8) =>
        Culprits.GroupBy(c => (c.Kind, c.Display.ToLowerInvariant()))
                .Select(g => new BootCulpritSummary
                {
                    Kind = g.Key.Kind, Name = g.First().Display, Occurrences = g.Count(),
                    AverageDelaySeconds = g.Average(c => c.DelaySeconds), MaxDelaySeconds = g.Max(c => c.DelaySeconds),
                    LastSeenUtc = g.Max(c => c.TimeUtc),
                })
                .OrderByDescending(s => s.AverageDelaySeconds * s.Occurrences).Take(max).ToList();
}

/// <summary>
/// Windows start-up time, read from the log Windows itself keeps (Microsoft-Windows-Diagnostics-Performance/Operational):
/// event 100 for each full start, events 101 to 110 for what made a start slower. Nothing is measured or estimated by
/// FrameCore. Fast Startup resumes (hybrid shutdown) are not all recorded there by Windows.
/// </summary>
public static class BootPerformance
{
    public const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public static BootHistory Read(int maxBoots = 30, int days = 90)
    {
        var h = new BootHistory();
        if (!OperatingSystem.IsWindows()) { h.Unavailable = true; return h; }
        try
        {
            var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
            var xpath = $"*[System[(EventID=100 or (EventID>=101 and EventID<=110)) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
            var q = new System.Diagnostics.Eventing.Reader.EventLogQuery(LogName, System.Diagnostics.Eventing.Reader.PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(q);
            var read = 0;
            for (var e = reader.ReadEvent(); e != null && read < 2000; e = reader.ReadEvent(), read++)
            {
                using (e)
                {
                    string xml;
                    try { xml = e.ToXml(); } catch { continue; }
                    if (e.Id == 100) { if (h.Boots.Count < maxBoots && ParseBoot(xml) is { } b) h.Boots.Add(b); }
                    else if (ParseCulprit(e.Id, xml) is { } c) h.Culprits.Add(c);
                }
            }
        }
        catch (UnauthorizedAccessException) { h.AccessDenied = true; }
        catch (System.Diagnostics.Eventing.Reader.EventLogNotFoundException) { h.Unavailable = true; }
        catch (System.Diagnostics.Eventing.Reader.EventLogException ex) when (ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase)) { h.AccessDenied = true; }
        catch (Exception ex) { h.Error = ex.Message; Log.Warn("Boot", ex.Message); }
        return h;
    }

    private static Dictionary<string, string> Data(string xml, out DateTime timeUtc)
    {
        var doc = XDocument.Parse(xml);
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in doc.Descendants().Where(x => x.Name.LocalName == "Data"))
            if (el.Attribute("Name")?.Value is { } n) d[n] = el.Value.Trim();
        var created = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
        timeUtc = DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;
        return d;
    }

    private static double? Ms(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && x >= 0 ? x / 1000.0 : null;

    private static bool Flag(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");

    public static BootRecord? ParseBoot(string xml)
    {
        try
        {
            var d = Data(xml, out var created);
            if (Ms(d, "BootTime") is not { } total || Ms(d, "MainPathBootTime") is not { } main) return null;
            var start = d.TryGetValue("BootStartTime", out var s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var st)
                ? st : created;
            return new BootRecord
            {
                StartUtc = start, TotalSeconds = total, MainPathSeconds = main,
                PostBootSeconds = Ms(d, "BootPostBootTime") ?? Math.Max(0, total - main),
                SignInWaitSeconds = Ms(d, "UserLogonWaitDuration") is { } w && w > 0 ? w : null,
                StartupApps = d.TryGetValue("BootNumStartupApps", out var a) && int.TryParse(a, out var n) ? n : null,
                AfterUpdate = Flag(d, "BootIsRebootAfterInstall"),
                FlaggedSlow = Flag(d, "BootIsDegradation"),
            };
        }
        catch { return null; }
    }

    public static BootCulpritKind KindOf(int eventId) => eventId switch
    {
        101 => BootCulpritKind.Application,
        102 => BootCulpritKind.Driver,
        103 => BootCulpritKind.Service,
        109 => BootCulpritKind.Device,
        _ => BootCulpritKind.Other,
    };

    public static BootCulprit? ParseCulprit(int eventId, string xml)
    {
        try
        {
            var d = Data(xml, out var created);
            var name = d.GetValueOrDefault("Name") ?? d.GetValueOrDefault("FileName") ?? "";
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new BootCulprit
            {
                TimeUtc = created, EventId = eventId, Kind = KindOf(eventId), Name = name.Trim().Split('\\', '/').Last(),
                FriendlyName = d.GetValueOrDefault("FriendlyName"),
                TotalSeconds = Ms(d, "TotalTime") ?? 0, DelaySeconds = Ms(d, "DegradationTime") ?? 0,
            };
        }
        catch { return null; }
    }
}
