using System.Globalization;
using System.Runtime.InteropServices;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.WindowsOptimization.Platform;

namespace FrameCore.WindowsOptimization.Executors;

public sealed class ServiceExecutor : IActionExecutor
{
    private readonly IServiceConfigurator _svc;
    public ServiceExecutor(IServiceConfigurator svc) => _svc = svc;
    public string Type => "service";
    public bool RequiresElevation(ActionDefinition a) => true;

    public ActionState Read(ActionDefinition a)
    {
        if (!_svc.Exists(a.Name!)) return new ActionState { Matches = true, Current = Loc.T("state.serviceAbsent"), Target = a.StartType! };
        var cur = _svc.GetStartType(a.Name!);
        return new ActionState { Matches = cur == a.StartType, Current = Loc.T("startType." + cur), Target = Loc.T("startType." + a.StartType) };
    }

    public StateSnapshot Capture(ActionDefinition a) =>
        _svc.Exists(a.Name!) ? StateSnapshot.Of(_svc.GetStartType(a.Name!)) : StateSnapshot.Absent();

    public void Apply(ActionDefinition a)
    {
        if (!_svc.Exists(a.Name!)) return;
        _svc.SetStartType(a.Name!, a.StartType!);
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        if (!s.Existed || !_svc.Exists(a.Name!)) return;
        if (s.Value is "Boot" or "System") return; // never touched by FrameCore
        _svc.SetStartType(a.Name!, s.Value!);
    }

    public bool Verify(ActionDefinition a, StateSnapshot s) => !s.Existed || !_svc.Exists(a.Name!) || _svc.GetStartType(a.Name!) == s.Value;
}

public sealed class TaskExecutor : IActionExecutor
{
    private readonly ITaskSchedulerAccess _tasks;
    public TaskExecutor(ITaskSchedulerAccess tasks) => _tasks = tasks;
    public string Type => "task";
    public bool RequiresElevation(ActionDefinition a) => a.Path!.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

    private static string Label(bool e) => Loc.T(e ? "state.enabled" : "state.disabled");

    public ActionState Read(ActionDefinition a)
    {
        var cur = _tasks.IsEnabled(a.Path!);
        if (cur == null) return new ActionState { Matches = true, Current = Loc.T("state.taskAbsent"), Target = Label(a.Enabled!.Value) };
        return new ActionState { Matches = cur == a.Enabled, Current = Label(cur.Value), Target = Label(a.Enabled!.Value) };
    }

    public StateSnapshot Capture(ActionDefinition a) =>
        _tasks.IsEnabled(a.Path!) is { } e ? StateSnapshot.Of(e ? "1" : "0") : StateSnapshot.Absent();

    public void Apply(ActionDefinition a) { if (_tasks.IsEnabled(a.Path!) != null) _tasks.SetEnabled(a.Path!, a.Enabled!.Value); }
    public void Restore(ActionDefinition a, StateSnapshot s) { if (s.Existed && _tasks.IsEnabled(a.Path!) != null) _tasks.SetEnabled(a.Path!, s.Value == "1"); }
    public bool Verify(ActionDefinition a, StateSnapshot s) => !s.Existed || _tasks.IsEnabled(a.Path!) == (s.Value == "1");
}

/// <summary>Power plan setting (AC and/or DC index) on the active scheme; restore writes back to the same scheme.</summary>
public sealed class PowerSettingExecutor : IActionExecutor
{
    private readonly IPowerAccess _power;
    public PowerSettingExecutor(IPowerAccess power) => _power = power;
    public string Type => "powerSetting";
    public bool RequiresElevation(ActionDefinition a) => true;

    private Guid SchemeOf(ActionDefinition a) => a.Scheme is { } s ? Guid.Parse(s) : _power.GetActiveScheme();

    /// <summary>Settings Windows refused to write because they do not exist on this machine (e.g. CPU power management in a VM).</summary>
    private static readonly HashSet<Guid> MissingSettings = new();

    public ActionState Read(ActionDefinition a)
    {
        var scheme = SchemeOf(a);
        var sub = Guid.Parse(a.Subgroup!); var set = Guid.Parse(a.Setting!);
        lock (MissingSettings) if (MissingSettings.Contains(set)) return ActionState.Absent(Loc.T("power.settingNotOnMachine"));
        var ac = _power.ReadAc(scheme, sub, set);
        var dc = _power.ReadDc(scheme, sub, set);
        if (ac == null && dc == null) return ActionState.Unreadable(Loc.T("state.powerSettingMissing"));
        var matches = (a.Ac == null || ac == (uint)a.Ac) && (a.Dc == null || dc == (uint)a.Dc);
        string Fmt(uint? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "?";
        return new ActionState
        {
            Matches = matches,
            Current = $"{Loc.T("power.ac")} {Fmt(ac)}" + (a.Dc != null ? $" / {Loc.T("power.dc")} {Fmt(dc)}" : ""),
            Target = (a.Ac != null ? $"{Loc.T("power.ac")} {a.Ac}" : "") + (a.Dc != null ? $" / {Loc.T("power.dc")} {a.Dc}" : ""),
        };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var scheme = SchemeOf(a);
        var sub = Guid.Parse(a.Subgroup!); var set = Guid.Parse(a.Setting!);
        var snap = StateSnapshot.Of(null);
        snap.Extra = new() { ["scheme"] = scheme.ToString() };
        if (_power.ReadAc(scheme, sub, set) is { } ac) snap.Extra["ac"] = ac.ToString(CultureInfo.InvariantCulture);
        if (_power.ReadDc(scheme, sub, set) is { } dc) snap.Extra["dc"] = dc.ToString(CultureInfo.InvariantCulture);
        return snap;
    }

    public void Apply(ActionDefinition a)
    {
        var scheme = SchemeOf(a);
        var sub = Guid.Parse(a.Subgroup!); var set = Guid.Parse(a.Setting!);
        try
        {
            if (a.Ac is { } ac) _power.WriteAc(scheme, sub, set, (uint)ac);
            if (a.Dc is { } dc) _power.WriteDc(scheme, sub, set, (uint)dc);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            lock (MissingSettings) MissingSettings.Add(set);
            throw new NotApplicableException(Loc.T("power.settingNotOnMachine"));
        }
        if (scheme == _power.GetActiveScheme()) _power.SetActiveScheme(scheme); // re-apply so changes take effect
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        var scheme = Guid.Parse(s.Extra!["scheme"]);
        if (!_power.SchemeExists(scheme)) return;
        var sub = Guid.Parse(a.Subgroup!); var set = Guid.Parse(a.Setting!);
        if (a.Ac != null && s.Extra.TryGetValue("ac", out var ac)) _power.WriteAc(scheme, sub, set, uint.Parse(ac, CultureInfo.InvariantCulture));
        if (a.Dc != null && s.Extra.TryGetValue("dc", out var dc)) _power.WriteDc(scheme, sub, set, uint.Parse(dc, CultureInfo.InvariantCulture));
        if (scheme == _power.GetActiveScheme()) _power.SetActiveScheme(scheme);
    }

    public bool Verify(ActionDefinition a, StateSnapshot s)
    {
        var scheme = Guid.Parse(s.Extra!["scheme"]);
        if (!_power.SchemeExists(scheme)) return true;
        var sub = Guid.Parse(a.Subgroup!); var set = Guid.Parse(a.Setting!);
        var okAc = a.Ac == null || !s.Extra.TryGetValue("ac", out var ac) || _power.ReadAc(scheme, sub, set)?.ToString(CultureInfo.InvariantCulture) == ac;
        var okDc = a.Dc == null || !s.Extra.TryGetValue("dc", out var dc) || _power.ReadDc(scheme, sub, set)?.ToString(CultureInfo.InvariantCulture) == dc;
        return okAc && okDc;
    }
}

/// <summary>Activates a power plan; duplicates hidden templates (Ultimate Performance) with their own GUID when missing.</summary>
public sealed class PowerSchemeExecutor : IActionExecutor
{
    private readonly IPowerAccess _power;
    public PowerSchemeExecutor(IPowerAccess power) => _power = power;
    public string Type => "powerScheme";
    public bool RequiresElevation(ActionDefinition a) => a.DuplicateFrom != null && !_power.SchemeExists(Guid.Parse(a.Scheme!));

    public ActionState Read(ActionDefinition a)
    {
        var active = _power.GetActiveScheme();
        var target = Guid.Parse(a.Scheme!);
        var name = _power.ListSchemes().FirstOrDefault(s => s.Guid == active)?.Name ?? PowerGuids.FriendlyScheme(active);
        return new ActionState { Matches = active == target, Current = name, Target = PowerGuids.FriendlyScheme(target) };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var snap = StateSnapshot.Of(_power.GetActiveScheme().ToString());
        snap.Extra = new() { ["targetExisted"] = _power.SchemeExists(Guid.Parse(a.Scheme!)) ? "1" : "0" };
        return snap;
    }

    public void Apply(ActionDefinition a)
    {
        var target = Guid.Parse(a.Scheme!);
        if (!_power.SchemeExists(target))
        {
            if (a.DuplicateFrom == null) throw new InvalidOperationException(Loc.T("power.schemeMissing"));
            try { _power.DuplicateScheme(Guid.Parse(a.DuplicateFrom), target); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 183)
            {
                // Hidden scheme already present (not listed): it is simply activated, and never deleted on restore.
                Log.Info("Power", $"Scheme {target} already exists (hidden); activating it");
            }
        }
        try { _power.SetActiveScheme(target); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 50)
        {
            throw new NotApplicableException(Loc.T("power.schemeNotSupported"));
        }
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        var previous = Guid.Parse(s.Value!);
        if (_power.SchemeExists(previous)) _power.SetActiveScheme(previous);
        var target = Guid.Parse(a.Scheme!);
        if (s.Extra?.GetValueOrDefault("targetExisted") == "0" && _power.SchemeExists(target) && _power.GetActiveScheme() != target)
        {
            try { _power.DeleteScheme(target); } catch { /* keeping an inactive plan is harmless */ }
        }
    }

    public bool Verify(ActionDefinition a, StateSnapshot s) => _power.GetActiveScheme() == Guid.Parse(s.Value!);
}

/// <summary>Windows 11 power mode slider (overlay). Uses undocumented powrprof exports → catalog marks it "Probable".</summary>
public sealed class PowerOverlayExecutor : IActionExecutor
{
    private readonly IPowerAccess _power;
    public PowerOverlayExecutor(IPowerAccess power) => _power = power;
    public string Type => "powerOverlay";
    public bool RequiresElevation(ActionDefinition a) => false;

    public ActionState Read(ActionDefinition a)
    {
        var cur = _power.GetOverlay();
        if (cur == null) return ActionState.Unreadable(Loc.T("power.overlayUnavailable"));
        var target = Guid.Parse(a.Scheme!);
        return new ActionState { Matches = cur == target, Current = PowerGuids.FriendlyOverlay(cur.Value), Target = PowerGuids.FriendlyOverlay(target) };
    }

    public StateSnapshot Capture(ActionDefinition a) => _power.GetOverlay() is { } g ? StateSnapshot.Of(g.ToString()) : StateSnapshot.Absent();
    public void Apply(ActionDefinition a) => _power.SetOverlay(Guid.Parse(a.Scheme!));
    public void Restore(ActionDefinition a, StateSnapshot s) { if (s.Existed) _power.SetOverlay(Guid.Parse(s.Value!)); }
    public bool Verify(ActionDefinition a, StateSnapshot s) => !s.Existed || _power.GetOverlay() == Guid.Parse(s.Value!);
}

/// <summary>Removes forced BCD overrides ({current}). Only whitelisted elements; never adds an override.</summary>
public sealed class BcdRemoveExecutor : IActionExecutor
{
    public string Type => "bcdRemove";
    public bool RequiresElevation(ActionDefinition a) => true;
    public bool ReadRequiresElevation(ActionDefinition a) => true;

    private static readonly string[] YesWords = { "yes", "oui", "ja", "sí", "si", "sim", "tak", "да", "evet", "是", "はい" };

    public static Dictionary<string, string> ParseCurrentEntry(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('-')) continue;
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) result[parts[0]] = parts[1].Trim();
        }
        return result;
    }

    private static Dictionary<string, string> Current()
    {
        var r = SystemTools.Run(SystemTool.Bcdedit, new[] { "/enum", "{current}" });
        if (!r.Success) throw new InvalidOperationException("bcdedit: " + r.StdOut + r.StdErr);
        return ParseCurrentEntry(r.StdOut);
    }

    public ActionState Read(ActionDefinition a)
    {
        var map = Current();
        var present = map.TryGetValue(a.Name!, out var v);
        return new ActionState
        {
            Matches = !present,
            Current = present ? Loc.T("bcd.forced", v) : Loc.T("state.windowsDefault"),
            Target = Loc.T("state.windowsDefault"),
        };
    }

    public StateSnapshot Capture(ActionDefinition a) =>
        Current().TryGetValue(a.Name!, out var v) ? StateSnapshot.Of(NormalizeValue(v)) : StateSnapshot.Absent();

    public static string NormalizeValue(string v)
    {
        var lower = v.Trim().ToLowerInvariant();
        if (YesWords.Contains(lower)) return "true";
        if (lower is "no" or "non" or "nein" or "não" or "nie" or "нет" or "hayır" or "否" or "いいえ") return "false";
        return v.Trim();
    }

    public void Apply(ActionDefinition a)
    {
        if (!Current().ContainsKey(a.Name!)) return; // already Windows' default
        var r = SystemTools.Run(SystemTool.Bcdedit, new[] { "/deletevalue", "{current}", a.Name! });
        if (!r.Success) throw new InvalidOperationException("bcdedit: " + r.StdOut + r.StdErr);
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        if (!s.Existed) return;
        var r = SystemTools.Run(SystemTool.Bcdedit, new[] { "/set", "{current}", a.Name!, s.Value! });
        if (!r.Success) throw new InvalidOperationException("bcdedit: " + r.StdOut + r.StdErr);
    }

    public bool Verify(ActionDefinition a, StateSnapshot s)
    {
        var map = Current();
        return s.Existed ? map.TryGetValue(a.Name!, out var v) && NormalizeValue(v) == s.Value : !map.ContainsKey(a.Name!);
    }
}

/// <summary>SystemParametersInfo settings that take effect immediately (no sign-out).</summary>
public sealed class SpiExecutor : IActionExecutor
{
    private const uint SPI_GETMOUSE = 0x0003, SPI_SETMOUSE = 0x0004;
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043;
    private const uint SPI_GETMENUSHOWDELAY = 0x006A, SPI_SETMENUSHOWDELAY = 0x006B;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    public string Type => "spi";
    public bool RequiresElevation(ActionDefinition a) => false;

    public static string Get(string name)
    {
        switch (name)
        {
            case "mouseAcceleration":
            {
                var v = new int[3];
                if (!SystemParametersInfoW(SPI_GETMOUSE, 0, v, 0)) throw new System.ComponentModel.Win32Exception();
                return $"{v[0]},{v[1]},{v[2]}";
            }
            case "clientAreaAnimation":
            {
                if (!SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, out bool b, 0)) throw new System.ComponentModel.Win32Exception();
                return b ? "1" : "0";
            }
            case "menuShowDelay":
            {
                if (!SystemParametersInfoW(SPI_GETMENUSHOWDELAY, 0, out uint ms, 0)) throw new System.ComponentModel.Win32Exception();
                return ms.ToString(CultureInfo.InvariantCulture);
            }
            default: throw new NotSupportedException(name);
        }
    }

    public static void Set(string name, string value)
    {
        const uint flags = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;
        bool ok = name switch
        {
            "mouseAcceleration" => SystemParametersInfoW(SPI_SETMOUSE, 0, value.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(), flags),
            "clientAreaAnimation" => SystemParametersInfoW(SPI_SETCLIENTAREAANIMATION, 0, (IntPtr)(value == "1" ? 1 : 0), flags),
            "menuShowDelay" => SystemParametersInfoW(SPI_SETMENUSHOWDELAY, uint.Parse(value, CultureInfo.InvariantCulture), IntPtr.Zero, flags),
            _ => throw new NotSupportedException(name),
        };
        if (!ok) throw new System.ComponentModel.Win32Exception();
    }

    public ActionState Read(ActionDefinition a)
    {
        var cur = Get(a.Name!);
        return new ActionState { Matches = cur == a.ValueAsString(), Current = Describe(a.Name!, cur), Target = Describe(a.Name!, a.ValueAsString()!) };
    }

    private static string Describe(string name, string v) => name switch
    {
        "mouseAcceleration" => v == "0,0,0" ? Loc.T("state.disabled") : Loc.T("state.enabledWith", v),
        "clientAreaAnimation" => Loc.T(v == "1" ? "state.enabled" : "state.disabled"),
        "menuShowDelay" => v + " ms",
        _ => v,
    };

    public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of(Get(a.Name!));
    public void Apply(ActionDefinition a) => Set(a.Name!, a.ValueAsString()!);
    public void Restore(ActionDefinition a, StateSnapshot s) => Set(a.Name!, s.Value!);
    public bool Verify(ActionDefinition a, StateSnapshot s) => Get(a.Name!) == s.Value;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint uiParam, int[] pvParam, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint uiParam, out bool pvParam, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint uiParam, out uint pvParam, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint uiParam, IntPtr pvParam, uint flags);
}

/// <summary>Windows optional features (whitelisted names only) via DISM with a fixed argument vector.</summary>
public sealed class OptionalFeatureExecutor : IActionExecutor
{
    public string Type => "optionalFeature";
    public bool RequiresElevation(ActionDefinition a) => true;

    public static bool? IsEnabled(string feature)
    {
        var row = Wmi.First($"SELECT InstallState FROM Win32_OptionalFeature WHERE Name='{feature.Replace("'", "")}'");
        return row.Int("InstallState") switch { 1 => true, 2 or 3 => false, _ => null };
    }

    public ActionState Read(ActionDefinition a)
    {
        var cur = IsEnabled(a.Name!);
        if (cur == null) return ActionState.Unreadable(Loc.Unknown);
        return new ActionState { Matches = cur == a.Enabled, Current = Loc.T(cur.Value ? "state.enabled" : "state.disabled"), Target = Loc.T(a.Enabled == true ? "state.enabled" : "state.disabled") };
    }

    public StateSnapshot Capture(ActionDefinition a) => IsEnabled(a.Name!) is { } e ? StateSnapshot.Of(e ? "1" : "0") : StateSnapshot.Absent();
    public void Apply(ActionDefinition a) => Set(a.Name!, a.Enabled == true);
    public void Restore(ActionDefinition a, StateSnapshot s) { if (s.Existed) Set(a.Name!, s.Value == "1"); }
    public bool Verify(ActionDefinition a, StateSnapshot s) => !s.Existed || IsEnabled(a.Name!) == (s.Value == "1") || PendingReboot(a.Name!);

    private static bool PendingReboot(string feature) => Wmi.First($"SELECT InstallState FROM Win32_OptionalFeature WHERE Name='{feature}'").Int("InstallState") is null or 4;

    private static void Set(string feature, bool enable)
    {
        var args = enable
            ? new[] { "/Online", "/Enable-Feature", "/FeatureName:" + feature, "/All", "/NoRestart", "/Quiet" }
            : new[] { "/Online", "/Disable-Feature", "/FeatureName:" + feature, "/NoRestart", "/Quiet" };
        var r = SystemTools.Run(SystemTool.Dism, args, TimeSpan.FromMinutes(20));
        if (r.ExitCode is not (0 or 3010)) throw new InvalidOperationException($"DISM exit code {r.ExitCode}");
    }
}
