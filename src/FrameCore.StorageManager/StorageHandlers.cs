using System.Globalization;
using System.Management;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.StorageManager;

/// <summary>Safe temporary files cleanup: only files older than 24 h, locked files skipped, shader caches never touched.</summary>
public sealed class TempCleanupHandler : IActionHandler
{
    private readonly bool _system;
    public TempCleanupHandler(bool system) => _system = system;
    public string Name => _system ? "cleanup.temp.system" : "cleanup.temp.user";
    public bool RequiresElevation(ActionDefinition a) => _system;

    public IEnumerable<string> Targets()
    {
        if (_system)
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            yield return Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive");
            yield return Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue");
        }
        else
        {
            yield return Path.GetTempPath();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, "CrashDumps");
            yield return Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive");
        }
    }

    public static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    private IEnumerable<FileInfo> Files()
    {
        var cutoff = DateTime.UtcNow - MinAge;
        foreach (var dir in Targets().Where(Directory.Exists))
        {
            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(dir).EnumerateFiles("*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System });
            }
            catch { continue; }
            foreach (var f in files)
            {
                bool old;
                try { old = f.LastWriteTimeUtc < cutoff && f.LastAccessTimeUtc < cutoff; } catch { continue; }
                if (old) yield return f;
            }
        }
    }

    public long ReclaimableBytes() => Files().Sum(f => { try { return f.Length; } catch { return 0L; } });

    public ActionState Read(ActionDefinition a)
    {
        var mb = ReclaimableBytes() / 1048576.0;
        return new ActionState { Matches = mb < 1, Current = Loc.T("cleanup.reclaimable", mb.ToString("0", CultureInfo.CurrentCulture)), Target = Loc.T("cleanup.target") };
    }

    public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of(ReclaimableBytes().ToString(CultureInfo.InvariantCulture));

    public void Apply(ActionDefinition a)
    {
        long freed = 0; int deleted = 0, skipped = 0;
        foreach (var f in Files().ToList())
        {
            try { var len = f.Length; f.Delete(); freed += len; deleted++; }
            catch { skipped++; }
        }
        // Remove empty sub-directories (never the target roots).
        foreach (var dir in Targets().Where(Directory.Exists))
        {
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(s => s.Length))
                    try { if (!Directory.EnumerateFileSystemEntries(sub).Any() && Directory.GetLastWriteTimeUtc(sub) < DateTime.UtcNow - MinAge) Directory.Delete(sub); } catch { }
            }
            catch { }
        }
        Log.Info("Cleanup", $"{Name}: {deleted} files deleted ({freed / 1048576} MB), {skipped} skipped (in use)");
    }

    public void Restore(ActionDefinition a, StateSnapshot s) => throw new NotSupportedException(Loc.T("engine.notReversible"));
    public bool Verify(ActionDefinition a, StateSnapshot s) => true;
}

/// <summary>ReTrim of SSD volumes through defrag.exe /L (what Windows' scheduled optimization does for SSDs).</summary>
public sealed class RetrimHandler : IActionHandler
{
    public string Name => "storage.retrim";
    public bool RequiresElevation(ActionDefinition a) => true;

    private static List<string> SsdVolumes()
    {
        var parts = Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage);
        var ssd = Wmi.Query("SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk", Wmi.Storage)
            .Where(d => d.Int("MediaType") == 4 || d.Int("BusType") == 17).Select(d => d.Str("DeviceId")).ToHashSet();
        return parts.Where(p => ssd.Contains(p.Str("DiskNumber")) && p.Int("DriveLetter") is > 0)
                    .Select(p => ((char)p.Int("DriveLetter")!.Value).ToString().ToUpperInvariant() + ":")
                    .Where(l => { try { var d = new DriveInfo(l); return d.IsReady && d.DriveFormat == "NTFS"; } catch { return false; } })
                    .Distinct().ToList();
    }

    public ActionState Read(ActionDefinition a) =>
        new() { Matches = false, Current = string.Join(", ", SsdVolumes()), Target = Loc.T("storage.retrim.target") };

    public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of(string.Join(",", SsdVolumes()));

    public void Apply(ActionDefinition a)
    {
        foreach (var v in SsdVolumes())
        {
            var r = SystemTools.Run(SystemTool.Defrag, new[] { v, "/L" }, TimeSpan.FromMinutes(15));
            if (!r.Success) Log.Warn("Retrim", $"{v}: exit {r.ExitCode}");
        }
    }

    public void Restore(ActionDefinition a, StateSnapshot s) => throw new NotSupportedException(Loc.T("engine.notReversible"));
    public bool Verify(ActionDefinition a, StateSnapshot s) => true;
}

/// <summary>Repairs a disabled page file by returning to "system managed". Custom page files are respected.</summary>
public sealed class PagefileHandler : IActionHandler
{
    public string Name => "storage.pagefile.auto";
    public bool RequiresElevation(ActionDefinition a) => true;

    private static (bool? auto, List<string> settings) State()
    {
        var auto = Wmi.First("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").Bool("AutomaticManagedPagefile");
        var settings = Wmi.Query("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting")
            .Select(r => $"{r.Str("Name")}|{r.Int("InitialSize")}|{r.Int("MaximumSize")}").ToList();
        return (auto, settings);
    }

    public ActionState Read(ActionDefinition a)
    {
        var (auto, settings) = State();
        if (auto == null) return ActionState.Unreadable(Loc.Unknown);
        var disabled = auto == false && settings.Count == 0;
        if (auto == false && settings.Count > 0) return ActionState.Absent(Loc.T("pagefile.notDisabled"));
        return new ActionState
        {
            Matches = !disabled,
            Current = auto == true ? Loc.T("pagefile.auto") : disabled ? Loc.T("pagefile.disabled") : Loc.T("pagefile.custom", string.Join(", ", settings.Select(s => s.Split('|')[0]))),
            Target = Loc.T("pagefile.auto"),
        };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var (auto, settings) = State();
        var s = StateSnapshot.Of(auto == true ? "1" : "0");
        s.Extra = new() { ["settings"] = string.Join(";", settings) };
        return s;
    }

    private static void SetAuto(bool value)
    {
        using var cls = new ManagementClass("Win32_ComputerSystem");
        foreach (ManagementObject mo in cls.GetInstances())
        {
            using (mo)
            {
                mo["AutomaticManagedPagefile"] = value;
                mo.Put();
            }
        }
    }

    public void Apply(ActionDefinition a)
    {
        var (auto, settings) = State();
        if (auto == false && settings.Count > 0) throw new NotApplicableException(Loc.T("pagefile.notDisabled"));
        SetAuto(true);
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        SetAuto(s.Value == "1");
        if (s.Value == "1") return;
        var had = (s.Extra?.GetValueOrDefault("settings") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = State().settings.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Remove settings that did not exist before (created by the automatic mode)…
        using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileSetting"))
            foreach (ManagementObject mo in searcher.Get())
                using (mo)
                {
                    var key = $"{mo["Name"]}|{mo["InitialSize"]}|{mo["MaximumSize"]}";
                    if (!had.Contains(key)) mo.Delete();
                }
        // …and recreate the custom page files that the automatic mode removed (name, initial and maximum size; 0/0 = system managed).
        foreach (var entry in had.Where(h => !now.Contains(h)))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0])) continue;
            using var cls = new ManagementClass("Win32_PageFileSetting");
            using var inst = cls.CreateInstance();
            inst["Name"] = parts[0];
            inst["InitialSize"] = uint.TryParse(parts[1], out var i) ? i : 0u;
            inst["MaximumSize"] = uint.TryParse(parts[2], out var m) ? m : 0u;
            inst.Put();
        }
    }

    public bool Verify(ActionDefinition a, StateSnapshot s)
    {
        var (auto, settings) = State();
        if (auto != (s.Value == "1")) return false;
        if (s.Value == "1") return true;
        var had = (s.Extra?.GetValueOrDefault("settings") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return had.SequenceEqual(settings.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class HibernateHandler : IActionHandler
{
    public string Name => "storage.hibernate.off";
    public bool RequiresElevation(ActionDefinition a) => true;
    private readonly IRegistryAccess _reg;
    public HibernateHandler(IRegistryAccess reg) => _reg = reg;

    private bool? Enabled() => _reg.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") is { } v ? RegistryCodec.Encode(v) == "1" : null;

    public ActionState Read(ActionDefinition a)
    {
        var e = Enabled();
        if (e == null) return ActionState.Absent(Loc.T("hibernate.unavailable"));
        long size = 0;
        try { var f = new FileInfo(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "hiberfil.sys")); if (f.Exists) size = f.Length; } catch { }
        return new ActionState { Matches = e == false, Current = Loc.T(e.Value ? "state.enabled" : "state.disabled") + (size > 0 ? $" ({size / 1073741824.0:0.0} Go)" : ""), Target = Loc.T("state.disabled") };
    }

    public StateSnapshot Capture(ActionDefinition a) => Enabled() is { } e ? StateSnapshot.Of(e ? "1" : "0") : StateSnapshot.Absent();
    public void Apply(ActionDefinition a) => Run(false);
    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        if (s.Existed && s.Value == "1") Run(true);
        else if (!s.Existed) _reg.DeleteValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
    }

    public bool Verify(ActionDefinition a, StateSnapshot s) => s.Existed ? Enabled() == (s.Value == "1") : Enabled() == null;

    private static void Run(bool on)
    {
        var r = SystemTools.Run(SystemTool.Powercfg, new[] { "/hibernate", on ? "on" : "off" });
        if (!r.Success) throw new InvalidOperationException("powercfg: " + r.StdOut + r.StdErr);
    }
}
