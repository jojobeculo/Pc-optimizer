using FrameCore.Core.Catalog;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.Core.Engine;

public sealed class TweakEvaluation
{
    public TweakDefinition Tweak { get; init; } = null!;
    public TweakStatus Status { get; init; }
    public bool Compatible { get; init; }
    public IReadOnlyList<string> IncompatibilityReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ActionState> ActionStates { get; init; } = Array.Empty<ActionState>();
    public bool NeedsElevation { get; init; }
    public string CurrentSummary => string.Join(" ; ", ActionStates.Select(s => s.Current));
    public string TargetSummary => string.Join(" ; ", ActionStates.Select(s => s.Target));
}

public sealed class ApplyResult
{
    public bool Success { get; init; }
    public bool RolledBack { get; init; }
    /// <summary>The change does not exist on this machine; nothing was left modified.</summary>
    public bool NotApplicable { get; init; }
    /// <summary>All actions were read back with their target value (null when some action cannot be verified).</summary>
    public bool? Verified => ChangeSet == null || !Success ? null
        : ChangeSet.Entries.Any(e => e.ApplyVerified == false) ? false
        : ChangeSet.Entries.All(e => e.ApplyVerified == true) ? true : null;
    public string? Error { get; init; }
    public ChangeSet? ChangeSet { get; init; }
    public RestartRequirement Restart { get; init; }
}

public sealed class RestoreResult
{
    public bool Success { get; init; }
    public int Restored { get; init; }
    public int Failed { get; init; }
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Applies catalog optimizations transactionally:
/// capture real state → persist journal → apply each action → on failure roll back already applied
/// actions in reverse order → verify. Restores always use the captured state.
/// </summary>
public sealed class TweakEngine
{
    private readonly IActionDispatcher _dispatcher;
    private readonly IChangeJournal _journal;
    private readonly object _gate = new();

    public TweakEngine(IActionDispatcher dispatcher, IChangeJournal journal)
    {
        _dispatcher = dispatcher;
        _journal = journal;
    }

    public IChangeJournal Journal => _journal;

    public TweakEvaluation Evaluate(TweakDefinition tweak, MachineFacts facts)
    {
        var compat = ConditionEvaluator.Evaluate(tweak.Conditions, facts);
        var states = new List<ActionState>();
        var needsElevation = false;
        foreach (var a in tweak.Actions)
        {
            needsElevation |= _dispatcher.NeedsElevation(a);
            try { states.Add(_dispatcher.Read(a, new ActionContext(tweak.Id, "catalog"))); }
            catch (Exception ex) { states.Add(ActionState.Unreadable(ex.Message)); }
        }

        TweakStatus status;
        var reasons = compat.FailedReasons.ToList();
        if (states.Any(s => s.NotApplicable))
        {
            compat = compat with { Passed = false };
            reasons.AddRange(states.Where(s => s.NotApplicable).Select(s => s.Error ?? Loc.T("engine.notApplicable")).Distinct());
        }
        if (!compat.Passed) status = TweakStatus.Unsupported;
        else if (states.Any(s => !s.Readable)) status = states.All(s => !s.Readable) ? TweakStatus.Unknown : TweakStatus.Partial;
        else if (states.All(s => s.Matches)) status = TweakStatus.Applied;
        else if (states.Any(s => s.Matches)) status = TweakStatus.Partial;
        else status = TweakStatus.NotApplied;

        // Stateless maintenance operations are never "applied": they can always be run again.
        if (!tweak.Reversible && compat.Passed && status == TweakStatus.Applied) status = TweakStatus.NotApplied;

        return new TweakEvaluation
        {
            Tweak = tweak, Status = status, Compatible = compat.Passed, IncompatibilityReasons = reasons,
            ActionStates = states, NeedsElevation = needsElevation,
        };
    }

    public ApplyResult Apply(TweakDefinition tweak, MachineFacts facts, string? batchId = null, IProgress<string>? progress = null)
    {
        var compat = ConditionEvaluator.Evaluate(tweak.Conditions, facts);
        if (!compat.Passed)
            return new ApplyResult { Success = false, Error = Loc.T("engine.incompatible", string.Join(" ; ", compat.FailedReasons)) };
        if (tweak.Confidence == Confidence.NotRecommended)
            Log.Warn("Engine", $"Applying not-recommended tweak {tweak.Id} on explicit user request");

        var ctx = new ActionContext(tweak.Id, "catalog");
        var cs = new ChangeSet
        {
            BatchId = batchId, TweakId = tweak.Id, Origin = "catalog", Category = tweak.Category, Title = tweak.Name,
            Restart = tweak.Restart, Reversible = tweak.Reversible,
        };
        return ApplyChangeSet(cs, tweak.Actions, ctx, progress);
    }

    /// <summary>Generic transactional application, also used for dynamic changes (startup entries, services…).</summary>
    public ApplyResult ApplyChangeSet(ChangeSet cs, IReadOnlyList<ActionDefinition> actions, ActionContext ctx, IProgress<string>? progress = null)
    {
        lock (_gate)
        {
            // 1. Capture the real state of every target before touching anything.
            try
            {
                for (var i = 0; i < actions.Count; i++)
                {
                    var a = actions[i];
                    var snap = _dispatcher.Capture(a, ctx);
                    cs.Entries.Add(new ChangeEntry { Index = i, Action = a, Before = snap });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Engine", $"Capture failed for {cs.TweakId ?? cs.Title.En}", ex);
                return new ApplyResult { Success = false, Error = Loc.T("engine.captureFailed", ex.Message) };
            }

            // 2. Persist before modification: a crash mid-way still leaves a restorable record.
            cs.Status = ChangeSetStatus.Pending;
            _journal.Save(cs);

            // 3. Apply sequentially; roll back on the first failure.
            foreach (var entry in cs.Entries)
            {
                try
                {
                    progress?.Report(entry.Action.ToString());
                    _dispatcher.Apply(entry.Action, ctx);
                    entry.Applied = true;
                    _journal.Save(cs);
                }
                catch (Exception ex)
                {
                    entry.Error = ex.Message;
                    var notApplicable = ex is NotApplicableException;
                    if (notApplicable) Log.Info("Engine", $"{cs.TweakId} action #{entry.Index} not applicable on this machine: {ex.Message}");
                    else Log.Error("Engine", $"Apply failed for {cs.TweakId} action #{entry.Index} ({entry.Action}); rolling back", ex);
                    // The failing action may have been partially written before throwing: it is restored too.
                    var rollbackErrors = RollBack(cs, ctx, failed: entry);
                    cs.Status = rollbackErrors == 0 ? ChangeSetStatus.RolledBack : ChangeSetStatus.Failed;
                    cs.Error = ex.Message;
                    _journal.Save(cs);
                    return new ApplyResult
                    {
                        Success = false, RolledBack = rollbackErrors == 0, NotApplicable = notApplicable && rollbackErrors == 0, ChangeSet = cs,
                        Error = notApplicable && rollbackErrors == 0 ? Loc.T("engine.notApplicableDetail", ex.Message)
                            : Loc.T(rollbackErrors == 0 ? "engine.applyFailedRolledBack" : "engine.applyFailedRollbackIncomplete", ex.Message),
                    };
                }
            }

            // 4. Read every value back: a change Windows silently ignored or overrode is reported, never assumed.
            foreach (var entry in cs.Entries)
            {
                try
                {
                    var st = _dispatcher.Read(entry.Action, ctx);
                    entry.ObservedAfterApply = st.Current;
                    entry.ApplyVerified = st.Readable ? st.Matches : null;
                }
                catch (Exception ex) { entry.ApplyVerified = null; entry.ObservedAfterApply = ex.Message; }
            }
            if (!cs.Reversible) foreach (var e in cs.Entries) e.ApplyVerified = null; // maintenance operations have no target state

            cs.Status = ChangeSetStatus.Applied;
            _journal.Save(cs);
            var notEffective = cs.Entries.Count(e => e.ApplyVerified == false);
            if (notEffective > 0) Log.Warn("Engine", $"Applied {cs.TweakId ?? cs.Title.En} but {notEffective} action(s) did not read back the target value");
            else Log.Info("Engine", $"Applied {cs.TweakId ?? cs.Title.En} ({cs.Entries.Count} action(s), verified)");
            return new ApplyResult { Success = true, ChangeSet = cs, Restart = cs.Restart };
        }
    }

    private int RollBack(ChangeSet cs, ActionContext ctx, ChangeEntry? failed = null)
    {
        var errors = 0;
        foreach (var entry in cs.Entries.Where(e => e.Applied || ReferenceEquals(e, failed)).Reverse())
        {
            try
            {
                _dispatcher.Restore(entry.Action, entry.Before!, ctx);
                entry.Restored = true;
                entry.Verified = SafeVerify(entry, ctx);
            }
            catch (Exception ex)
            {
                errors++;
                entry.Error = (entry.Error == null ? "" : entry.Error + " | ") + "rollback: " + ex.Message;
                Log.Error("Engine", $"Rollback failed for {cs.TweakId} action #{entry.Index}", ex);
            }
        }
        return errors;
    }

    private bool SafeVerify(ChangeEntry e, ActionContext ctx)
    {
        try { return _dispatcher.Verify(e.Action, e.Before!, ctx); }
        catch { return false; }
    }

    /// <summary>Restores one change set to the exact captured state, in reverse order, and verifies it.</summary>
    public RestoreResult Restore(ChangeSet cs)
    {
        lock (_gate)
        {
            var result = new RestoreResult();
            if (!cs.Reversible)
            {
                result.Errors.Add(Loc.T("engine.notReversible"));
                return new RestoreResult { Success = false, Errors = result.Errors };
            }
            var ctx = new ActionContext(cs.TweakId, cs.Origin);
            int ok = 0, failed = 0;
            foreach (var entry in cs.Entries.Where(e => e.Applied && !e.Restored).Reverse())
            {
                try
                {
                    _dispatcher.Restore(entry.Action, entry.Before!, ctx);
                    entry.Restored = true;
                    entry.Verified = SafeVerify(entry, ctx);
                    if (!entry.Verified) result.Errors.Add(Loc.T("engine.verifyFailed", entry.Action.ToString()));
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    entry.Error = ex.Message;
                    result.Errors.Add($"{entry.Action}: {ex.Message}");
                    Log.Error("Engine", $"Restore failed for {cs.TweakId} #{entry.Index}", ex);
                }
            }
            cs.Status = failed == 0 ? ChangeSetStatus.Restored : ChangeSetStatus.PartiallyRestored;
            cs.RestoredUtc = DateTime.UtcNow;
            _journal.Save(cs);
            return new RestoreResult { Success = failed == 0, Restored = ok, Failed = failed, Errors = result.Errors };
        }
    }

    /// <summary>
    /// Re-writes the actions of an applied change set whose value is not (or no longer) the target, keeping the original
    /// captured state untouched so that a later restore still returns to the real initial value.
    /// Returns true when every action reads back its target afterwards.
    /// </summary>
    public bool Reapply(ChangeSet cs, string reason)
    {
        lock (_gate)
        {
            var ctx = new ActionContext(cs.TweakId, cs.Origin);
            cs.RepairAttempts++;
            foreach (var entry in cs.Entries.Where(e => e.Applied && !e.Restored))
            {
                try
                {
                    var st = _dispatcher.Read(entry.Action, ctx);
                    if (st.Readable && !st.Matches) _dispatcher.Apply(entry.Action, ctx);
                    var after = _dispatcher.Read(entry.Action, ctx);
                    entry.ObservedAfterApply = after.Current;
                    entry.ApplyVerified = after.Readable ? after.Matches : null;
                }
                catch (Exception ex) { entry.ApplyVerified = false; entry.Error = ex.Message; }
            }
            var ok = cs.Entries.Where(e => e.Applied && !e.Restored).All(e => e.ApplyVerified != false);
            cs.History.Add(new ChangeEvent { Kind = ok ? "repaired" : "repairFailed", Detail = reason });
            _journal.Save(cs);
            return ok;
        }
    }

    /// <summary>Re-reads every action of an applied change set (post-apply or post-reboot verification).</summary>
    public bool? Verify(ChangeSet cs)
    {
        var ctx = new ActionContext(cs.TweakId, cs.Origin);
        bool? all = true;
        foreach (var entry in cs.Entries.Where(e => e.Applied && !e.Restored))
        {
            try
            {
                var st = _dispatcher.Read(entry.Action, ctx);
                entry.ObservedAfterApply = st.Current;
                entry.ApplyVerified = st.Readable ? st.Matches : null;
            }
            catch (Exception ex) { entry.ApplyVerified = null; entry.ObservedAfterApply = ex.Message; }
            if (entry.ApplyVerified == false) all = false;
            else if (entry.ApplyVerified == null && all == true) all = null;
        }
        return all;
    }

    /// <summary>Restores several change sets, most recent first (category, profile batch or everything).</summary>
    public RestoreResult RestoreMany(IEnumerable<ChangeSet> sets)
    {
        int ok = 0, failed = 0;
        var errors = new List<string>();
        foreach (var cs in sets.Where(s => s.Reversible && s.Status is ChangeSetStatus.Applied or ChangeSetStatus.PartiallyRestored or ChangeSetStatus.Failed)
                               .OrderByDescending(s => s.CreatedUtc))
        {
            var r = Restore(cs);
            ok += r.Restored; failed += r.Failed; errors.AddRange(r.Errors);
        }
        return new RestoreResult { Success = failed == 0, Restored = ok, Failed = failed, Errors = errors };
    }

    /// <summary>Change sets still active (applied and not restored) for a tweak.</summary>
    public IEnumerable<ChangeSet> ActiveChangeSets(string tweakId) =>
        _journal.ChangeSets.Where(c => string.Equals(c.TweakId, tweakId, StringComparison.OrdinalIgnoreCase)
                                       && c.Status is ChangeSetStatus.Applied or ChangeSetStatus.PartiallyRestored);
}

/// <summary>Thrown by an executor when the target does not exist on this machine (not an error of FrameCore).</summary>
public sealed class NotApplicableException(string message) : Exception(message);
