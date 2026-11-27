using FrameCore.Core.Catalog;
using FrameCore.Core.Models;

namespace FrameCore.Core.Engine;

public sealed class PlanItem
{
    public TweakDefinition Tweak { get; init; } = null!;
    public TweakEvaluation Evaluation { get; init; } = null!;
    public PlanSection Section { get; init; }
    public bool SelectedByDefault { get; init; }
}

public sealed class OptimizationPlan
{
    public ProfileLevel Profile { get; init; }
    public List<PlanItem> Items { get; } = new();
    /// <summary>Compatible profile items already in the target state.</summary>
    public List<TweakEvaluation> AlreadyApplied { get; } = new();
    /// <summary>Profile items excluded because they are incompatible with this machine (with reasons).</summary>
    public List<TweakEvaluation> Excluded { get; } = new();
    /// <summary>Items left out because a conflicting optimisation is already active or preferred (item, kept id).</summary>
    public List<(TweakEvaluation Item, string KeptId)> Conflicting { get; } = new();
    public bool NeedsElevation => Items.Any(i => i.Evaluation.NeedsElevation);
    public bool NeedsReboot => Items.Any(i => i.Tweak.Restart == RestartRequirement.Reboot);
}

/// <summary>Turns a profile level into a concrete, machine-specific plan.</summary>
public static class PlanBuilder
{
    public static OptimizationPlan Build(ProfileLevel level, IEnumerable<TweakEvaluation> evaluations)
    {
        var plan = new OptimizationPlan { Profile = level };
        foreach (var ev in evaluations)
        {
            var t = ev.Tweak;
            // Extreme contains everything of Maximum plus its own settings.
            if (!t.Profiles.Contains(level) && !(level == ProfileLevel.Extreme && t.Profiles.Contains(ProfileLevel.Maximum))) continue;
            if (t.Confidence == Confidence.NotRecommended) continue; // never, whatever the catalog says
            if (!ev.Compatible) { plan.Excluded.Add(ev); continue; }
            if (ev.Status == TweakStatus.Applied) { plan.AlreadyApplied.Add(ev); continue; }

            var section = t.Confidence == Confidence.Experimental ? PlanSection.Experimental
                        : t.RequiresConsent ? PlanSection.Consent
                        : PlanSection.Standard;
            plan.Items.Add(new PlanItem
            {
                Tweak = t, Evaluation = ev, Section = section,
                // Experimental and consent-gated items are listed but never pre-selected — except in Extreme, which the
                // user chooses explicitly for a gaming-only PC (and confirms through a specific warning).
                SelectedByDefault = section == PlanSection.Standard || level == ProfileLevel.Extreme,
            });
        }
        plan.Items.Sort((a, b) => a.Section != b.Section ? a.Section.CompareTo(b.Section) : string.CompareOrdinal(a.Tweak.Category + a.Tweak.Id, b.Tweak.Category + b.Tweak.Id));

        // Conflicts: what is already active wins, then standard before consent before experimental items.
        var kept = plan.AlreadyApplied.Select(e => e.Tweak.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.ToList())
        {
            var clash = item.Tweak.Conflicts.FirstOrDefault(kept.Contains);
            if (clash != null) { plan.Items.Remove(item); plan.Conflicting.Add((item.Evaluation, clash)); }
            else kept.Add(item.Tweak.Id);
        }
        return plan;
    }
}
