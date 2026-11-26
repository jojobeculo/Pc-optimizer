using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.WindowsOptimization.Executors;

/// <summary>Registry values. "Absent" is a real state: restoring an absent value deletes it.</summary>
public sealed class RegistryExecutor : IActionExecutor
{
    private readonly IRegistryAccess _reg;
    public RegistryExecutor(IRegistryAccess reg) => _reg = reg;
    public string Type => "registry";

    public bool RequiresElevation(ActionDefinition a) =>
        a.Path!.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
        || a.Path.StartsWith(@"HKCU\Software\Policies", StringComparison.OrdinalIgnoreCase);

    public ActionState Read(ActionDefinition a)
    {
        var current = _reg.GetValue(a.Path!, a.Name!);
        var target = a.Delete ? Loc.T("state.absent") : a.ValueAsString() ?? "";
        if (current == null)
            return new ActionState { Matches = a.Delete, Current = Loc.T("state.absent"), Target = target };
        var cur = RegistryCodec.Encode(current);
        var matches = !a.Delete && RegistryCodec.AreEqual(a.ValueKind!, cur, NormalizeTarget(a));
        return new ActionState { Matches = matches, Current = Display(current.Kind, cur), Target = target };
    }

    private static string NormalizeTarget(ActionDefinition a) =>
        a.ValueKind is "DWord" or "QWord" ? RegistryCodec.ParseUnsigned(a.ValueAsString()!).ToString() : a.ValueAsString()!;

    private static string Display(string kind, string encoded) =>
        kind == "DWord" && uint.TryParse(encoded, out var u) && u > 9 ? $"{u} (0x{u:X})" : encoded;

    public StateSnapshot Capture(ActionDefinition a)
    {
        var v = _reg.GetValue(a.Path!, a.Name!);
        return v == null ? StateSnapshot.Absent() : StateSnapshot.Of(RegistryCodec.Encode(v), v.Kind);
    }

    public void Apply(ActionDefinition a)
    {
        if (a.Delete) { _reg.DeleteValue(a.Path!, a.Name!); return; }
        _reg.SetValue(a.Path!, a.Name!, a.ValueKind!, RegistryCodec.Decode(a.ValueKind!, a.ValueAsString()!));
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        if (!s.Existed) { _reg.DeleteValue(a.Path!, a.Name!); return; }
        _reg.SetValue(a.Path!, a.Name!, s.Kind!, RegistryCodec.Decode(s.Kind!, s.Value!));
    }

    public bool Verify(ActionDefinition a, StateSnapshot s)
    {
        var v = _reg.GetValue(a.Path!, a.Name!);
        if (!s.Existed) return v == null;
        return v != null && v.Kind == s.Kind && RegistryCodec.AreEqual(s.Kind!, RegistryCodec.Encode(v), s.Value);
    }
}

/// <summary>
/// One key=value pair inside the semicolon-separated DirectXUserGlobalSettings string
/// (Windows Settings → Display → Graphics: windowed games optimizations, VRR, Auto HDR).
/// </summary>
public sealed class DxSettingExecutor : IActionExecutor
{
    public const string KeyPath = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    public const string ValueName = "DirectXUserGlobalSettings";
    private readonly IRegistryAccess _reg;
    public DxSettingExecutor(IRegistryAccess reg) => _reg = reg;
    public string Type => "dxSetting";
    public bool RequiresElevation(ActionDefinition a) => false;

    public static Dictionary<string, string> Parse(string? s)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(s)) return d;
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) d[part[..eq]] = part[(eq + 1)..];
        }
        return d;
    }

    public static string Format(Dictionary<string, string> d) => string.Concat(d.Select(kv => $"{kv.Key}={kv.Value};"));

    private (bool valueExists, Dictionary<string, string> map) Load()
    {
        var v = _reg.GetValue(KeyPath, ValueName);
        return (v != null, Parse(v?.Data as string));
    }

    public ActionState Read(ActionDefinition a)
    {
        var (_, map) = Load();
        var has = map.TryGetValue(a.Name!, out var cur);
        return new ActionState
        {
            Matches = has && cur == a.ValueAsString(),
            Current = has ? $"{a.Name}={cur}" : Loc.T("state.default"),
            Target = $"{a.Name}={a.ValueAsString()}",
        };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var (exists, map) = Load();
        var snap = map.TryGetValue(a.Name!, out var cur) ? StateSnapshot.Of(cur) : StateSnapshot.Absent();
        snap.Extra = new() { ["valueExisted"] = exists ? "1" : "0" };
        return snap;
    }

    public void Apply(ActionDefinition a)
    {
        var (_, map) = Load();
        map[a.Name!] = a.ValueAsString() ?? "";
        _reg.SetValue(KeyPath, ValueName, "String", Format(map));
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        var (_, map) = Load();
        if (s.Existed) map[a.Name!] = s.Value ?? "";
        else map.Remove(a.Name!);
        if (map.Count == 0 && s.Extra?.GetValueOrDefault("valueExisted") == "0") _reg.DeleteValue(KeyPath, ValueName);
        else _reg.SetValue(KeyPath, ValueName, "String", Format(map));
    }

    public bool Verify(ActionDefinition a, StateSnapshot s)
    {
        var (_, map) = Load();
        var has = map.TryGetValue(a.Name!, out var cur);
        return s.Existed ? has && cur == s.Value : !has;
    }
}
