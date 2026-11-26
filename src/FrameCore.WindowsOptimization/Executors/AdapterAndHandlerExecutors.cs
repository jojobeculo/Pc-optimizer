using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.WindowsOptimization.Executors;

/// <summary>
/// Standardized NDIS keywords (e.g. *EEE) of physical Ethernet adapters, written in the adapter's
/// class key — the same storage used by Device Manager and Set-NetAdapterAdvancedProperty.
/// Takes effect after an adapter restart or reboot.
/// </summary>
public sealed class NetAdapterPropertyExecutor : IActionExecutor
{
    public const string NetClass = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string Absent = "<absent>";
    private readonly IRegistryAccess _reg;
    public NetAdapterPropertyExecutor(IRegistryAccess reg) => _reg = reg;
    public string Type => "netAdapterProperty";
    public bool RequiresElevation(ActionDefinition a) => true;

    /// <summary>Class subkeys of physical Ethernet adapters that expose the keyword.</summary>
    public IReadOnlyList<string> MatchingAdapters(string keyword, string? filter)
    {
        var result = new List<string>();
        foreach (var sub in _reg.GetSubKeyNames(NetClass).Where(s => s.Length == 4 && s.All(char.IsDigit)))
        {
            var path = NetClass + "\\" + sub;
            var ifType = _reg.GetValue(path, "*IfType");
            var media = _reg.GetValue(path, "*PhysicalMediaType");
            var isEthernet = ifType != null && RegistryCodec.Encode(ifType) == "6" && (media == null || RegistryCodec.Encode(media) is "14" or "0");
            if (filter == "ethernet" && !isEthernet) continue;
            if (!_reg.KeyExists(path + @"\Ndi\Params\" + keyword)) continue;
            result.Add(path);
        }
        return result;
    }

    private string? CurrentValue(string adapterPath, string keyword) =>
        _reg.GetValue(adapterPath, keyword) is { } v ? RegistryCodec.Encode(v) : null;

    private string? DefaultValue(string adapterPath, string keyword) =>
        _reg.GetValue(adapterPath + @"\Ndi\Params\" + keyword, "default") is { } v ? RegistryCodec.Encode(v) : null;

    public ActionState Read(ActionDefinition a)
    {
        var adapters = MatchingAdapters(a.Name!, a.Filter);
        if (adapters.Count == 0) return new ActionState { Matches = true, Current = Loc.T("net.noMatchingAdapter"), Target = a.ValueAsString()! };
        var values = adapters.Select(p => CurrentValue(p, a.Name!) ?? DefaultValue(p, a.Name!) ?? "?").ToList();
        return new ActionState
        {
            Matches = values.All(v => v == a.ValueAsString()),
            Current = string.Join(", ", values.Distinct()),
            Target = a.ValueAsString()!,
        };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var snap = StateSnapshot.Of(null);
        snap.Extra = MatchingAdapters(a.Name!, a.Filter).ToDictionary(p => p, p => CurrentValue(p, a.Name!) ?? Absent);
        return snap;
    }

    public void Apply(ActionDefinition a)
    {
        foreach (var p in MatchingAdapters(a.Name!, a.Filter))
            _reg.SetValue(p, a.Name!, "String", a.ValueAsString()!);
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        foreach (var (path, value) in s.Extra ?? new())
        {
            if (value == Absent) _reg.DeleteValue(path, a.Name!);
            else _reg.SetValue(path, a.Name!, "String", value);
        }
    }

    public bool Verify(ActionDefinition a, StateSnapshot s) =>
        (s.Extra ?? new()).All(kv => (CurrentValue(kv.Key, a.Name!) ?? Absent) == kv.Value);
}

public sealed class HandlerExecutor : IActionExecutor
{
    private readonly Dictionary<string, IActionHandler> _handlers;
    public HandlerExecutor(IEnumerable<IActionHandler> handlers) =>
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);

    public string Type => "handler";
    private IActionHandler H(ActionDefinition a) =>
        _handlers.TryGetValue(a.Name!, out var h) ? h : throw new NotSupportedException($"Unknown handler {a.Name}");

    public IEnumerable<string> Names => _handlers.Keys;
    public bool RequiresElevation(ActionDefinition a) => H(a).RequiresElevation(a);
    public ActionState Read(ActionDefinition a) => H(a).Read(a);
    public StateSnapshot Capture(ActionDefinition a) => H(a).Capture(a);
    public void Apply(ActionDefinition a) => H(a).Apply(a);
    public void Restore(ActionDefinition a, StateSnapshot s) => H(a).Restore(a, s);
    public bool Verify(ActionDefinition a, StateSnapshot s) => H(a).Verify(a, s);
}
