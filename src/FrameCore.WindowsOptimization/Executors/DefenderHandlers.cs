using System.Globalization;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.WindowsOptimization.Executors;

/// <summary>The few Microsoft Defender preferences FrameCore may change (Extreme level only, with consent).</summary>
public interface IDefenderPreferences
{
    /// <summary>False when Defender is not the active antivirus or its management interface is absent.</summary>
    bool Available { get; }
    /// <summary>Null when the list cannot be read (Windows shows exclusions to administrators only).</summary>
    IReadOnlyList<string>? ExclusionPaths();
    int? ScanCpuLimit();
    void AddExclusionPaths(IReadOnlyList<string> paths);
    void RemoveExclusionPaths(IReadOnlyList<string> paths);
    void SetScanCpuLimit(int percent);
}

/// <summary>Microsoft Defender's official management interface (WMI class MSFT_MpPreference) — no PowerShell, no registry.</summary>
public sealed class WmiDefenderPreferences : IDefenderPreferences
{
    private const string Ns = @"root\Microsoft\Windows\Defender";

    private static ManagementObject? Pref()
    {
        using var s = new ManagementObjectSearcher(Ns, "SELECT * FROM MSFT_MpPreference");
        return s.Get().Cast<ManagementObject>().FirstOrDefault();
    }

    public bool Available
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { using var p = Pref(); return p != null; } catch { return false; }
        }
    }

    public IReadOnlyList<string>? ExclusionPaths()
    {
        using var p = Pref();
        var v = p?["ExclusionPath"] as string[];
        if (v == null) return Array.Empty<string>();
        // Without administrator rights Windows returns a placeholder instead of the list.
        return v.Any(x => x.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)) ? null : v;
    }

    public int? ScanCpuLimit()
    {
        using var p = Pref();
        return p?["ScanAvgCPULoadFactor"] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;
    }

    private static void Invoke(string method, string property, object value)
    {
        using var cls = new ManagementClass(Ns, "MSFT_MpPreference", null);
        using var input = cls.GetMethodParameters(method);
        input[property] = value;
        using var output = cls.InvokeMethod(method, input, null);
        var rc = output?["ReturnValue"] is { } r ? Convert.ToInt64(r, CultureInfo.InvariantCulture) : 0;
        if (rc != 0) throw new InvalidOperationException($"Microsoft Defender refused the change ({method} {property}: 0x{rc:X})");
    }

    public void AddExclusionPaths(IReadOnlyList<string> paths) { if (paths.Count > 0) Invoke("Add", "ExclusionPath", paths.ToArray()); }
    public void RemoveExclusionPaths(IReadOnlyList<string> paths) { if (paths.Count > 0) Invoke("Remove", "ExclusionPath", paths.ToArray()); }
    public void SetScanCpuLimit(int percent) => Invoke("Set", "ScanAvgCPULoadFactor", (byte)Math.Clamp(percent, 5, 100));
}

/// <summary>
/// Scheduled scans may start while playing if the PC was not idle: their CPU use is capped (Windows default 50 %).
/// Real-time protection is not changed.
/// </summary>
public sealed class DefenderScanCpuHandler : IActionHandler
{
    private readonly IDefenderPreferences _d;
    public DefenderScanCpuHandler(IDefenderPreferences d) => _d = d;
    public string Name => "defender.scanCpuLimit";
    public bool RequiresElevation(ActionDefinition a) => true;
    private static int Target(ActionDefinition a) => int.TryParse(a.ValueAsString(), out var v) ? v : 20;

    public ActionState Read(ActionDefinition a)
    {
        if (!_d.Available) return ActionState.Absent(Loc.T("defender.unavailable"));
        var cur = _d.ScanCpuLimit();
        if (cur == null) return ActionState.Unreadable(Loc.T("engine.needsElevationToRead"));
        return new ActionState { Matches = cur <= Target(a), Current = Loc.T("defender.scanCpu", cur), Target = Loc.T("defender.scanCpu", Target(a)) };
    }

    public StateSnapshot Capture(ActionDefinition a) =>
        _d.ScanCpuLimit() is { } v ? StateSnapshot.Of(v.ToString(CultureInfo.InvariantCulture)) : throw new InvalidOperationException(Loc.T("defender.unavailable"));

    public void Apply(ActionDefinition a)
    {
        if (!_d.Available) throw new NotApplicableException(Loc.T("defender.unavailable"));
        _d.SetScanCpuLimit(Target(a));
    }

    public void Restore(ActionDefinition a, StateSnapshot s) { if (s.Existed && int.TryParse(s.Value, out var v)) _d.SetScanCpuLimit(v); }
    public bool Verify(ActionDefinition a, StateSnapshot s) => !s.Existed || _d.ScanCpuLimit()?.ToString(CultureInfo.InvariantCulture) == s.Value;
}

/// <summary>
/// Game library folders excluded from Defender's on-access scanning (less scanning while a game streams its files).
/// Only folders of the installed launchers are added; restoring removes exactly the folders FrameCore added.
/// </summary>
public sealed class DefenderGameFolderExclusionsHandler : IActionHandler
{
    private readonly IDefenderPreferences _d;
    private readonly Func<IReadOnlyList<string>> _gameFolders;
    public DefenderGameFolderExclusionsHandler(IDefenderPreferences d, Func<IReadOnlyList<string>> gameFolders) { _d = d; _gameFolders = gameFolders; }
    public string Name => "defender.gameFolderExclusions";
    public bool RequiresElevation(ActionDefinition a) => true;

    private sealed record Snapshot(List<string> Before, List<string> Planned);
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;
    private static string Norm(string p) => p.Trim().TrimEnd('\\', '/');

    public ActionState Read(ActionDefinition a)
    {
        if (!_d.Available) return ActionState.Absent(Loc.T("defender.unavailable"));
        var folders = _gameFolders();
        if (folders.Count == 0) return ActionState.Absent(Loc.T("defender.noGameFolder"));
        var excluded = _d.ExclusionPaths();
        if (excluded == null) return ActionState.Unreadable(Loc.T("engine.needsElevationToRead"));
        var set = excluded.Select(Norm).ToHashSet(Cmp);
        var done = folders.Count(f => set.Contains(Norm(f)));
        return new ActionState
        {
            Matches = done == folders.Count,
            Current = Loc.T("defender.exclusions.current", done, folders.Count),
            Target = string.Join("\n", folders),
        };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var before = _d.ExclusionPaths() ?? throw new UnauthorizedAccessException(Loc.T("engine.needsElevationToRead"));
        var planned = _gameFolders().Where(f => !before.Select(Norm).Contains(Norm(f), Cmp)).ToList();
        return StateSnapshot.Of(JsonSerializer.Serialize(new Snapshot(before.ToList(), planned)));
    }

    private static Snapshot Parse(StateSnapshot s) =>
        s.Existed && s.Value != null ? JsonSerializer.Deserialize<Snapshot>(s.Value) ?? new Snapshot(new(), new()) : new Snapshot(new(), new());

    public void Apply(ActionDefinition a)
    {
        if (!_d.Available) throw new NotApplicableException(Loc.T("defender.unavailable"));
        var before = (_d.ExclusionPaths() ?? Array.Empty<string>()).Select(Norm).ToHashSet(Cmp);
        var add = _gameFolders().Where(f => Directory.Exists(f) && !before.Contains(Norm(f))).ToList();
        if (add.Count == 0) throw new NotApplicableException(Loc.T("defender.noGameFolder"));
        _d.AddExclusionPaths(add);
    }

    /// <summary>Removes only the folders this change added; exclusions that existed before stay.</summary>
    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        var snap = Parse(s);
        var current = (_d.ExclusionPaths() ?? Array.Empty<string>()).Select(Norm).ToHashSet(Cmp);
        var before = snap.Before.Select(Norm).ToHashSet(Cmp);
        var remove = snap.Planned.Where(p => current.Contains(Norm(p)) && !before.Contains(Norm(p))).ToList();
        _d.RemoveExclusionPaths(remove);
    }

    public bool Verify(ActionDefinition a, StateSnapshot s)
    {
        var snap = Parse(s);
        var current = (_d.ExclusionPaths() ?? Array.Empty<string>()).Select(Norm).ToHashSet(Cmp);
        var before = snap.Before.Select(Norm).ToHashSet(Cmp);
        return snap.Planned.All(p => !current.Contains(Norm(p)) || before.Contains(Norm(p))) && before.All(current.Contains);
    }
}

/// <summary>Install folders of the main game launchers on this PC (only folders that exist).</summary>
public static class GameLibraryFolders
{
    public static IReadOnlyList<string> Detect(IRegistryAccess reg)
    {
        var list = new List<string>();
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) list.Add(Path.GetFullPath(p).TrimEnd('\\')); }
        try
        {
            // Steam: every library declared in libraryfolders.vdf → steamapps\common.
            var steam = reg.GetValue(@"HKCU\Software\Valve\Steam", "SteamPath")?.Data?.ToString();
            if (steam != null)
            {
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                var roots = new List<string> { steam };
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                foreach (var r in roots) Add(Path.Combine(r, "steamapps", "common"));
            }
            // Epic Games: one manifest per installed game (Fortnite included).
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (Directory.Exists(manifests))
                foreach (var f in Directory.EnumerateFiles(manifests, "*.item"))
                    try { using var doc = JsonDocument.Parse(File.ReadAllText(f)); if (doc.RootElement.TryGetProperty("InstallLocation", out var loc)) Add(loc.GetString()); } catch { }
            // Other launchers: their default library folders, on every fixed drive.
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                Add(Path.Combine(d.RootDirectory.FullName, "Riot Games"));
                Add(Path.Combine(d.RootDirectory.FullName, "XboxGames"));
                Add(Path.Combine(d.RootDirectory.FullName, @"Program Files\EA Games"));
                Add(Path.Combine(d.RootDirectory.FullName, @"Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games"));
            }
        }
        catch (Exception ex) { Log.Warn("GameFolders", ex.Message); }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
