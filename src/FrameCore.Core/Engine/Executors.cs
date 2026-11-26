using FrameCore.Core.Models;

namespace FrameCore.Core.Engine;

/// <summary>
/// Executes one closed action type. Implementations must be deterministic and must capture the
/// exact previous state so that <see cref="Restore"/> brings the system back to it.
/// </summary>
public interface IActionExecutor
{
    string Type { get; }
    /// <summary>True when Apply/Restore need administrator rights for this action.</summary>
    bool RequiresElevation(ActionDefinition action);
    /// <summary>True when even reading the state needs administrator rights (e.g. bcdedit).</summary>
    bool ReadRequiresElevation(ActionDefinition action) => false;
    ActionState Read(ActionDefinition action);
    StateSnapshot Capture(ActionDefinition action);
    void Apply(ActionDefinition action);
    void Restore(ActionDefinition action, StateSnapshot snapshot);
    /// <summary>True when the current state equals the snapshot (post-restore verification).</summary>
    bool Verify(ActionDefinition action, StateSnapshot snapshot);
}

/// <summary>Custom, module-provided operations (cleanup, ReTrim, refresh rate…). Same contract as executors.</summary>
public interface IActionHandler
{
    string Name { get; }
    bool RequiresElevation(ActionDefinition action);
    ActionState Read(ActionDefinition action);
    StateSnapshot Capture(ActionDefinition action);
    void Apply(ActionDefinition action);
    void Restore(ActionDefinition action, StateSnapshot snapshot);
    bool Verify(ActionDefinition action, StateSnapshot snapshot);
}

public sealed class ExecutorRegistry
{
    private readonly Dictionary<string, IActionExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    public ExecutorRegistry(IEnumerable<IActionExecutor> executors)
    {
        foreach (var e in executors) _executors[e.Type] = e;
    }

    public IActionExecutor Get(string type) =>
        _executors.TryGetValue(type, out var e) ? e : throw new NotSupportedException($"No executor for action type '{type}'");

    public bool Has(string type) => _executors.ContainsKey(type);
    public IEnumerable<string> Types => _executors.Keys;
}

/// <summary>Context sent with every privileged request so the worker can validate it against its whitelist.</summary>
public sealed record ActionContext(string? TweakId, string Origin);

/// <summary>Routes actions to the local process or to the elevated worker.</summary>
public interface IActionDispatcher
{
    ActionState Read(ActionDefinition action, ActionContext context);
    StateSnapshot Capture(ActionDefinition action, ActionContext context);
    void Apply(ActionDefinition action, ActionContext context);
    void Restore(ActionDefinition action, StateSnapshot snapshot, ActionContext context);
    bool Verify(ActionDefinition action, StateSnapshot snapshot, ActionContext context);
    bool NeedsElevation(ActionDefinition action);
}

/// <summary>Executes everything in-process (used by the elevated worker itself and by tests).</summary>
public sealed class LocalDispatcher : IActionDispatcher
{
    private readonly ExecutorRegistry _registry;
    public LocalDispatcher(ExecutorRegistry registry) => _registry = registry;

    public ActionState Read(ActionDefinition a, ActionContext c) => _registry.Get(a.Type).Read(a);
    public StateSnapshot Capture(ActionDefinition a, ActionContext c) => _registry.Get(a.Type).Capture(a);
    public void Apply(ActionDefinition a, ActionContext c) => _registry.Get(a.Type).Apply(a);
    public void Restore(ActionDefinition a, StateSnapshot s, ActionContext c) => _registry.Get(a.Type).Restore(a, s);
    public bool Verify(ActionDefinition a, StateSnapshot s, ActionContext c) => _registry.Get(a.Type).Verify(a, s);
    public bool NeedsElevation(ActionDefinition a) => false;
}

/// <summary>Channel to the elevated worker (implemented by the elevation broker).</summary>
public interface IPrivilegedChannel
{
    bool IsAvailable { get; }
    T Send<T>(string operation, object payload, TimeSpan? timeout = null);
}

public sealed record PrivilegedActionRequest(string Op, ActionDefinition Action, StateSnapshot? Snapshot, ActionContext Context);

/// <summary>
/// Default dispatcher of the (non-elevated) UI process: runs locally when possible, otherwise sends a
/// typed request to the elevated worker, which re-validates it against the catalog and safety policy.
/// </summary>
public sealed class RoutingDispatcher : IActionDispatcher
{
    private readonly ExecutorRegistry _registry;
    private readonly Func<IPrivilegedChannel> _channel;
    private readonly bool _elevated;

    public RoutingDispatcher(ExecutorRegistry registry, Func<IPrivilegedChannel> channel, bool processIsElevated)
    {
        _registry = registry;
        _channel = channel;
        _elevated = processIsElevated;
    }

    public bool NeedsElevation(ActionDefinition a) => !_elevated && _registry.Get(a.Type).RequiresElevation(a);

    private bool Remote(ActionDefinition a, bool write) =>
        !_elevated && (write ? _registry.Get(a.Type).RequiresElevation(a) : _registry.Get(a.Type).ReadRequiresElevation(a));

    public ActionState Read(ActionDefinition a, ActionContext c)
    {
        if (Remote(a, write: false))
        {
            var ch = _channel();
            if (!ch.IsAvailable) return ActionState.Unreadable(Localization.Loc.T("engine.needsElevationToRead"));
            return ch.Send<ActionState>("action.read", new PrivilegedActionRequest("read", a, null, c));
        }
        return _registry.Get(a.Type).Read(a);
    }

    public StateSnapshot Capture(ActionDefinition a, ActionContext c) =>
        Remote(a, write: false) || Remote(a, write: true)
            ? _channel().Send<StateSnapshot>("action.capture", new PrivilegedActionRequest("capture", a, null, c))
            : WithFallback(() => _registry.Get(a.Type).Capture(a), () => _channel().Send<StateSnapshot>("action.capture", new PrivilegedActionRequest("capture", a, null, c)));

    public void Apply(ActionDefinition a, ActionContext c)
    {
        if (Remote(a, write: true)) { _channel().Send<bool>("action.apply", new PrivilegedActionRequest("apply", a, null, c)); return; }
        WithFallback(() => { _registry.Get(a.Type).Apply(a); return true; },
            () => _channel().Send<bool>("action.apply", new PrivilegedActionRequest("apply", a, null, c)));
    }

    public void Restore(ActionDefinition a, StateSnapshot s, ActionContext c)
    {
        if (Remote(a, write: true)) { _channel().Send<bool>("action.restore", new PrivilegedActionRequest("restore", a, s, c)); return; }
        WithFallback(() => { _registry.Get(a.Type).Restore(a, s); return true; },
            () => _channel().Send<bool>("action.restore", new PrivilegedActionRequest("restore", a, s, c)));
    }

    public bool Verify(ActionDefinition a, StateSnapshot s, ActionContext c) =>
        Remote(a, write: false)
            ? _channel().Send<bool>("action.verify", new PrivilegedActionRequest("verify", a, s, c))
            : _registry.Get(a.Type).Verify(a, s);

    /// <summary>Some operations unexpectedly need rights on some machines; retry once through the worker.</summary>
    private T WithFallback<T>(Func<T> local, Func<T> remote)
    {
        try { return local(); }
        catch (UnauthorizedAccessException) when (!_elevated) { return remote(); }
        catch (System.Security.SecurityException) when (!_elevated) { return remote(); }
    }
}
