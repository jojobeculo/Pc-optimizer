using FrameCore.Core;

namespace FrameCore.DriverManager;

public sealed class DriverChange
{
    public DateTime DetectedUtc { get; set; } = DateTime.UtcNow;
    public string Device { get; set; } = "";
    public string Category { get; set; } = "";
    public string? OldVersion { get; set; }
    public string? NewVersion { get; set; }
}

internal sealed class DriverHistoryFile
{
    public Dictionary<string, string> Last { get; set; } = new();
    public List<DriverChange> Changes { get; set; } = new();
}

/// <summary>
/// Remembers the versions of the main drivers between diagnostics (stored only on this PC) so that a driver update
/// can be related to a performance change and the user keeps an history.
/// </summary>
public static class DriverHistory
{
    private static readonly string[] Tracked = { "Gpu", "Chipset", "Ethernet", "WiFi", "Audio", "Storage" };
    private static string FilePath => Path.Combine(AppPaths.Root, "driver-history.json");

    public static List<DriverChange> Record(IEnumerable<DriverEntry> drivers)
    {
        DriverHistoryFile h;
        try { h = File.Exists(FilePath) ? Json.Read<DriverHistoryFile>(FilePath) ?? new() : new(); }
        catch (Exception ex) { Log.Warn("DriverHistory", ex.Message); h = new(); }
        var current = drivers.Where(d => Tracked.Contains(d.Category) && d.Version != null)
                             .GroupBy(d => d.DeviceName).ToDictionary(g => g.Key, g => g.First());
        var newChanges = new List<DriverChange>();
        if (h.Last.Count > 0)
            foreach (var (name, d) in current)
                if (h.Last.TryGetValue(name, out var old) && old != d.Version)
                    newChanges.Add(new DriverChange { Device = name, Category = d.Category, OldVersion = old, NewVersion = d.Version });
        h.Last = current.ToDictionary(kv => kv.Key, kv => kv.Value.Version!);
        h.Changes.InsertRange(0, newChanges);
        if (h.Changes.Count > 200) h.Changes.RemoveRange(200, h.Changes.Count - 200);
        try { Json.WriteAtomic(FilePath, h); } catch (Exception ex) { Log.Warn("DriverHistory", ex.Message); }
        return h.Changes;
    }
}
