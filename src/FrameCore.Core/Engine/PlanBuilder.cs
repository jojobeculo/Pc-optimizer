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
            if (!t.Profiles.Contains(level)) continue;
            if (t.Confidence == Confidence.NotRecommended) continue; // never, whatever the catalog says
            if (!ev.Compatible) { plan.Excluded.Add(ev); continue; }
            if (ev.Status == TweakStatus.Applied) { plan.AlreadyApplied.Add(ev); continue; }

            var section = t.Confidence == Confidence.Experimental ? PlanSection.Experimental
                        : t.RequiresConsent ? PlanSection.Consent
                        : PlanSection.Standard;
            plan.Items.Add(new PlanItem
            {
                Tweak = t, Evaluation = ev, Section = section,
                // Experimental and consent-gated items are listed but never pre-selected.
                SelectedByDefault = section == PlanSection.Standard,
            });
        }
        plan.Items.Sort((a, b) => a.Section != b.Section ? a.Section.CompareTo(b.Section) : string.CompareOrdinal(a.Tweak.Category + a.Tweak.Id, b.Tweak.Category + b.Tweak.Id));
        return plan;
    }
}
