using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Verification;

public enum VerifyStatus { Effective, Repaired, NotVerifiable, RolledBackNotEffective, RolledBackRegression, Reapplied, Kept }

public sealed class VerifyItem
{
    public string ChangeSetId { get; set; } = "";
    public string? TweakId { get; set; }
    public string Title { get; set; } = "";
    public VerifyStatus Status { get; set; }
    public int Attempts { get; set; }
    public string Explanation { get; set; } = "";
}

public sealed class VerifyRepairReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public string? BatchId { get; set; }
    public List<VerifyItem> Items { get; set; } = new();
    public QuickBenchComparison? Performance { get; set; }
    public List<string> Decisions { get; set; } = new();
    public int Effective => Items.Count(i => i.Status is VerifyStatus.Effective or VerifyStatus.Repaired or VerifyStatus.Reapplied or VerifyStatus.Kept);
}

/// <summary>
/// Auto Verify &amp; Repair.
/// 1. Every applied change is read back. 2. A value Windows did not keep is re-written at most twice, then the change
/// is restored. 3. Optionally, a quick benchmark after the changes is compared with the one before; a confirmed
/// regression of a component makes FrameCore restore the changes that can affect that component, re-measure, and keep
/// them restored only if the regression disappears (otherwise they are re-applied once: the cause lies elsewhere).
/// Nothing is ever retried indefinitely and every decision is written to the change history.
/// </summary>
public sealed class VerifyRepairService
{
    public const int MaxRepairAttempts = 2;
    private readonly TweakEngine _engine;
    private readonly Func<string, TweakDefinition?> _findTweak;

    public VerifyRepairService(TweakEngine engine, Func<string, TweakDefinition?> findTweak)
    {
        _engine = engine;
        _findTweak = findTweak;
    }

    /// <summary>Steps 1–2: read back and bounded repair.</summary>
    public VerifyRepairReport VerifyAndRepair(IEnumerable<ChangeSet> sets, string? batchId = null, IProgress<string>? progress = null)
    {
        var report = new VerifyRepairReport { BatchId = batchId };
        foreach (var cs in sets.Where(s => s.Status == ChangeSetStatus.Applied && s.Reversible))
        {
            progress?.Report(cs.Title.ToString());
            var item = new VerifyItem { ChangeSetId = cs.Id, TweakId = cs.TweakId, Title = cs.Title.ToString() };
            report.Items.Add(item);
            var ok = _engine.Verify(cs);
            if (ok == true) { item.Status = VerifyStatus.Effective; item.Explanation = Loc.T("verify.effective"); Event(cs, "verified", item.Explanation); continue; }
            if (ok == null) { item.Status = VerifyStatus.NotVerifiable; item.Explanation = Loc.T("verify.notVerifiable"); continue; }

            Event(cs, "notEffective", string.Join(" ; ", cs.Entries.Where(e => e.ApplyVerified == false).Select(e => $"{e.Action} → {e.ObservedAfterApply}")));
            var repaired = false;
            while (!repaired && cs.RepairAttempts < MaxRepairAttempts)
            {
                repaired = _engine.Reapply(cs, Loc.T("verify.repairAttempt", cs.RepairAttempts + 1, MaxRepairAttempts));
                item.Attempts = cs.RepairAttempts;
            }
            if (repaired) { item.Status = VerifyStatus.Repaired; item.Explanation = Loc.T("verify.repaired", item.Attempts); continue; }

            var restore = _engine.Restore(cs);
            item.Status = VerifyStatus.RolledBackNotEffective;
            item.Explanation = Loc.T("verify.rolledBackNotEffective", item.Attempts) + (restore.Success ? "" : " " + string.Join(" ; ", restore.Errors));
            Event(cs, "rolledBack", item.Explanation);
        }
        report.Decisions.Add(Loc.T("verify.summary", report.Effective, report.Items.Count));
        return report;
    }

    /// <summary>Which benchmark components a change can plausibly affect (network, privacy and comfort changes affect none).</summary>
    public static IReadOnlySet<BenchComponent> ComponentsAffectedBy(TweakDefinition t)
    {
        var set = new HashSet<BenchComponent>();
        var perf = t.ImpactAreas.Any(a => a is ImpactArea.Fps or ImpactArea.FrameTimes or ImpactArea.InputLatency or ImpactArea.Responsiveness);
        if (!perf) return set;
        switch (t.Category)
        {
            case "Cpu": set.Add(BenchComponent.Cpu); set.Add(BenchComponent.System); set.Add(BenchComponent.Memory); break;
            case "Gpu": case "Display": set.Add(BenchComponent.Gpu); break;
            case "Ram": set.Add(BenchComponent.Memory); break;
            case "Storage": set.Add(BenchComponent.Storage); break;
            default: set.Add(BenchComponent.System); set.Add(BenchComponent.Cpu); break;
        }
        return set;
    }

    /// <summary>
    /// Step 3. <paramref name="remeasure"/> runs the benchmark again for the given components only.
    /// </summary>
    public void CheckPerformance(VerifyRepairReport report, IReadOnlyList<ChangeSet> sets, QuickBenchResult before, QuickBenchResult after,
                                 Func<HashSet<BenchComponent>, QuickBenchResult> remeasure, MachineFactsProvider facts)
    {
        var cmp = QuickBenchComparer.Compare(before, after);
        report.Performance = cmp;
        var regressed = cmp.Regressions.Where(r => r.Confidence >= ResultConfidence.Medium).Select(r => r.Component).ToHashSet();
        if (regressed.Count == 0) { report.Decisions.Add(Loc.T("verify.noRegression")); return; }

        var suspects = sets.Where(s => s.Status == ChangeSetStatus.Applied && s.Reversible && s.TweakId != null)
                           .Where(s => _findTweak(s.TweakId!) is { } t && ComponentsAffectedBy(t).Overlaps(regressed)).ToList();
        var names = string.Join(", ", regressed.Select(c => Loc.T("bench.component." + c)));
        if (suspects.Count == 0) { report.Decisions.Add(Loc.T("verify.regressionUnrelated", names)); return; }

        // Attempt 1: restore the suspects and measure again.
        _engine.RestoreMany(suspects);
        foreach (var s in suspects) Event(s, "regression", Loc.T("verify.regressionRestored", names));
        var check = remeasure(regressed);
        var recheck = QuickBenchComparer.Compare(before, check);
        var stillRegressed = recheck.Regressions.Where(r => r.Confidence >= ResultConfidence.Medium).Select(r => r.Component).Intersect(regressed).Any();
        if (!stillRegressed)
        {
            foreach (var s in suspects)
                report.Items.Add(new VerifyItem { ChangeSetId = s.Id, TweakId = s.TweakId, Title = s.Title.ToString(), Status = VerifyStatus.RolledBackRegression, Attempts = 1,
                    Explanation = Loc.T("verify.rolledBackRegression", names) });
            report.Decisions.Add(Loc.T("verify.regressionFixed", names, suspects.Count));
            return;
        }

        // Attempt 2 (last): the regression persists without them, so they were not the cause: re-apply once.
        foreach (var s in suspects)
        {
            var t = _findTweak(s.TweakId!);
            if (t == null) continue;
            var r = _engine.Apply(t, facts(), s.BatchId);
            if (r.ChangeSet != null) Event(r.ChangeSet, "reapplied", Loc.T("verify.reappliedUnrelated", names));
            report.Items.Add(new VerifyItem { ChangeSetId = r.ChangeSet?.Id ?? s.Id, TweakId = s.TweakId, Title = s.Title.ToString(),
                Status = r.Success ? VerifyStatus.Reapplied : VerifyStatus.RolledBackRegression, Attempts = 2,
                Explanation = r.Success ? Loc.T("verify.reappliedUnrelated", names) : r.Error ?? "" });
        }
        report.Decisions.Add(Loc.T("verify.regressionPersists", names));
    }

    private void Event(ChangeSet cs, string kind, string detail)
    {
        cs.History.Add(new ChangeEvent { Kind = kind, Detail = detail });
        _engine.Journal.Save(cs);
    }
}

public delegate Core.Catalog.MachineFacts MachineFactsProvider();
