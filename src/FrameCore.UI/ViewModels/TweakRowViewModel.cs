using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.UI.ViewModels;

/// <summary>One catalog optimization as shown in plans and in Pro mode (all fields the user asked for).</summary>
public sealed partial class TweakRowViewModel : ObservableObject
{
    public TweakDefinition Tweak { get; }
    [ObservableProperty] private TweakEvaluation _evaluation;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _hasActiveChange;
    public PlanSection Section { get; init; }

    private readonly Func<TweakRowViewModel, Task>? _apply;
    private readonly Func<TweakRowViewModel, Task>? _restore;

    public TweakRowViewModel(TweakDefinition tweak, TweakEvaluation evaluation, Func<TweakRowViewModel, Task>? apply = null, Func<TweakRowViewModel, Task>? restore = null)
    {
        Tweak = tweak;
        _evaluation = evaluation;
        _apply = apply;
        _restore = restore;
    }

    public string Id => Tweak.Id;
    public string Name => Tweak.Name.ToString();
    public string Group => Tweak.Group.ToString();
    public string Description => Tweak.Description.ToString();
    public string Impact => Tweak.Impact.ToString();
    public string Regressions => Tweak.Regressions.ToString();
    public Confidence Confidence => Tweak.Confidence;
    public RiskLevel Risk => Tweak.Risk;
    public TweakKind Kind => Tweak.Kind;
    public RestartRequirement Restart => Tweak.Restart;
    public bool NeedsRestart => Tweak.Restart != RestartRequirement.None;
    public bool Reversible => Tweak.Reversible;
    public string Sources => string.Join("\n", Tweak.Sources);
    public string Objective => Tweak.Objective?.ToString() ?? "";
    public string PersistenceText => Loc.T("persistence." + Tweak.Persistence);
    public string ImpactAreasText => Tweak.ImpactAreas.Count == 0 ? "" : string.Join(", ", Tweak.ImpactAreas.Select(a => Loc.T("impact." + a)))
        + (Tweak.CanAffectFrameRate ? "" : " — " + Loc.T("tweak.noFps"));
    public string ConflictsText => Tweak.Conflicts.Count == 0 ? Loc.T("common.none") : string.Join(", ", Tweak.Conflicts);
    /// <summary>How FrameCore checks and undoes this change, derived from its action types.</summary>
    public string VerificationText => string.Join("\n", Tweak.Actions.Select(a => a.Type).Distinct().Select(t => Loc.T("method.verify." + t)));
    public string RestorationText => Tweak.Reversible
        ? string.Join("\n", Tweak.Actions.Select(a => a.Type).Distinct().Select(t => Loc.T("method.restore." + t)))
        : Loc.T("method.restore.none");
    public bool HasSources => Tweak.Sources.Count > 0;
    public TweakStatus Status => Evaluation.Status;
    public bool Compatible => Evaluation.Compatible;
    public string Compatibility => Evaluation.Compatible ? Loc.T("tweak.compatible") : string.Join("\n", Evaluation.IncompatibilityReasons);
    public string CurrentValue => Evaluation.ActionStates.Count == 0 ? "" : string.Join("\n", Evaluation.ActionStates.Select(s => s.Readable ? s.Current : "? " + s.Error));
    public string TargetValue => string.Join("\n", Evaluation.ActionStates.Select(s => s.Target));
    public bool NeedsElevation => Evaluation.NeedsElevation;
    public bool IsExperimental => Tweak.Confidence == Confidence.Experimental;
    public bool IsNotRecommended => Tweak.Confidence == Confidence.NotRecommended;
    public string ProfilesText => Tweak.Profiles.Count == 0 ? Loc.T("tweak.proOnly") : string.Join(", ", Tweak.Profiles.Select(p => Loc.T("profile." + p)));
    public bool CanApply => Compatible && (Status != TweakStatus.Applied || !Tweak.Reversible);
    /// <summary>Maintenance operations have no lasting state: "applied" means there is nothing to do right now.</summary>
    public string StatusLabel => !Tweak.Reversible && Status == TweakStatus.Applied ? Loc.T("status.nothingToDo") : Loc.T("status." + Status);
    public bool CanRestore => HasActiveChange && Tweak.Reversible;

    partial void OnEvaluationChanged(TweakEvaluation value)
    {
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(CurrentValue)); OnPropertyChanged(nameof(TargetValue));
        OnPropertyChanged(nameof(Compatible)); OnPropertyChanged(nameof(Compatibility)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(NeedsElevation));
    }

    partial void OnHasActiveChangeChanged(bool value) => OnPropertyChanged(nameof(CanRestore));

    [RelayCommand] private Task Apply() => _apply?.Invoke(this) ?? Task.CompletedTask;
    [RelayCommand] private Task Restore() => _restore?.Invoke(this) ?? Task.CompletedTask;
}

public sealed class PropertyRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public PropertyRow() { }
    public PropertyRow(string label, string? value) { Label = label; Value = string.IsNullOrWhiteSpace(value) ? Loc.Unknown : value; }
}
