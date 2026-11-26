using System.Text.Json;
using FrameCore.Core;
using FrameCore.Core.Localization;

namespace FrameCore.HardwareDetection;

public sealed class WindowsVersionEntry
{
    public int Build { get; set; }
    public string Version { get; set; } = "";
    public DateTime? EndHomePro { get; set; }
    public DateTime? EndEnterprise { get; set; }
    public DateTime? EndLtsc { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Servicing table (catalog/knowledge/windows-versions.json, source: Microsoft "Windows 11 release information").
/// Kept as data so it can be updated without code changes; unknown builds are reported as "Indéterminé".
/// </summary>
public static class WindowsVersions
{
    private static List<WindowsVersionEntry>? _entries;

    public static IReadOnlyList<WindowsVersionEntry> Entries => _entries ??= Load();

    private static List<WindowsVersionEntry> Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.CatalogDir, "knowledge", "windows-versions.json");
            if (File.Exists(path)) return JsonSerializer.Deserialize<List<WindowsVersionEntry>>(File.ReadAllText(path), Json.Options) ?? new();
        }
        catch (Exception ex) { Log.Warn("WinVer", ex.Message); }
        return new();
    }

    public static void UseEntries(List<WindowsVersionEntry> entries) => _entries = entries;

    public static (string? version, bool? ended, string note) Evaluate(int build, string? editionId, DateTime today)
    {
        var e = Entries.FirstOrDefault(x => x.Build == build);
        if (e == null)
        {
            var newest = Entries.Count > 0 ? Entries.Max(x => x.Build) : 0;
            return build > newest
                ? (null, false, Loc.T("os.servicing.newer"))
                : (null, null, Loc.T("os.servicing.unknown"));
        }
        var ed = editionId ?? "";
        DateTime? end = ed.Contains("EnterpriseS", StringComparison.OrdinalIgnoreCase) || ed.Contains("LTSC", StringComparison.OrdinalIgnoreCase) ? e.EndLtsc ?? e.EndEnterprise
                      : ed.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) || ed.StartsWith("Education", StringComparison.OrdinalIgnoreCase) || ed.StartsWith("IoT", StringComparison.OrdinalIgnoreCase) ? e.EndEnterprise
                      : e.EndHomePro;
        if (end == null) return (e.Version, null, Loc.T("os.servicing.unknown"));
        var ended = today.Date > end.Value.Date;
        return (e.Version, ended, ended ? Loc.T("os.servicing.ended", e.Version, end.Value) : Loc.T("os.servicing.supported", e.Version, end.Value));
    }
}
