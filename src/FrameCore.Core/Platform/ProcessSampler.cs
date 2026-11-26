using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace FrameCore.Core.Platform;

public sealed class ProcessSample
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public long PrivateBytes { get; set; }
    public long WorkingSet { get; set; }
    public double? CpuPercent { get; set; }
    public bool HasWindow { get; set; }
    public string? Path { get; set; }
    public int SessionId { get; set; }
}

/// <summary>Per-process CPU (delta of processor time between two samples) and memory.</summary>
public sealed class ProcessSampler
{
    private readonly Dictionary<int, (TimeSpan cpu, DateTime at)> _last = new();

    public List<ProcessSample> Sample(bool includePaths = false)
    {
        var now = DateTime.UtcNow;
        var list = new List<ProcessSample>();
        var seen = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id is 0 or 4) continue;
                    var s = new ProcessSample { Pid = p.Id, Name = p.ProcessName, SessionId = p.SessionId };
                    try { s.PrivateBytes = p.PrivateMemorySize64; s.WorkingSet = p.WorkingSet64; } catch { }
                    try { s.HasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { }
                    try
                    {
                        var cpu = p.TotalProcessorTime;
                        if (_last.TryGetValue(p.Id, out var prev))
                        {
                            var elapsed = (now - prev.at).TotalMilliseconds;
                            if (elapsed > 0) s.CpuPercent = Math.Max(0, (cpu - prev.cpu).TotalMilliseconds / (elapsed * Environment.ProcessorCount) * 100.0);
                        }
                        _last[p.Id] = (cpu, now);
                    }
                    catch { /* protected process */ }
                    if (includePaths) { try { s.Path = p.MainModule?.FileName; } catch { } }
                    seen.Add(p.Id);
                    list.Add(s);
                }
                catch { }
            }
        }
        foreach (var dead in _last.Keys.Where(k => !seen.Contains(k)).ToList()) _last.Remove(dead);
        return list;
    }
}

public sealed record EventRecordInfo(DateTime Time, int Id, string Provider, string Level, string Message);

/// <summary>Reads recent Windows events (WHEA hardware errors, display driver resets, memory diagnostics results).</summary>
public static class EventLogQuery
{
    public static List<EventRecordInfo> Query(string log, string xpath, int max = 50)
    {
        var list = new List<EventRecordInfo>();
        if (!OperatingSystem.IsWindows()) return list;
        try
        {
            var q = new System.Diagnostics.Eventing.Reader.EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            for (var e = reader.ReadEvent(); e != null && list.Count < max; e = reader.ReadEvent())
            {
                using (e)
                {
                    string msg;
                    try { msg = e.FormatDescription() ?? ""; } catch { msg = ""; }
                    list.Add(new EventRecordInfo(e.TimeCreated ?? DateTime.MinValue, e.Id, e.ProviderName ?? "", e.LevelDisplayName ?? e.Level?.ToString() ?? "", msg));
                }
            }
        }
        catch (Exception ex) { Log.Warn("EventLog", $"{log}: {ex.Message}"); }
        return list;
    }

    /// <summary>Events from a provider since a given time (UTC), e.g. WHEA-Logger during a stress test.</summary>
    public static List<EventRecordInfo> Since(string log, string provider, DateTime sinceUtc, int[]? ids = null)
    {
        var ms = (long)Math.Max(1000, (DateTime.UtcNow - sinceUtc).TotalMilliseconds);
        var idFilter = ids is { Length: > 0 } ? " and (" + string.Join(" or ", ids.Select(i => $"EventID={i}")) + ")" : "";
        return Query(log, $"*[System[Provider[@Name='{provider}']{idFilter} and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
    }
}
