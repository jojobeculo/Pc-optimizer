using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.StartupManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed partial class StartupSuggestion : ObservableObject
{
    public StartupEntry Entry { get; init; } = null!;
    [ObservableProperty] private bool _isSelected;
    public string Name => Entry.DisplayName ?? Entry.Name;
    public string Reason => Entry.Reason ?? "";
    public string Publisher => Entry.Publisher ?? Loc.Unknown;
}

/// <summary>Simple mode: three profiles turned into a concrete, reviewable plan before anything is changed.</summary>
public sealed partial class OptimizationViewModel : ObservableObject, IDiagnosticConsumer
{
    private readonly ShellViewModel _shell;

    [ObservableProperty] private ProfileLevel? _selectedProfile;
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private int _alreadyApplied;
    [ObservableProperty] private bool _createRestorePoint = true;
    [ObservableProperty] private bool _experimentalAcknowledged;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _needsElevation;
    [ObservableProperty] private int _lightCount;
    [ObservableProperty] private int _balancedCount;
    [ObservableProperty] private int _maximumCount;

    public ObservableCollection<TweakRowViewModel> Standard { get; } = new();
    public ObservableCollection<TweakRowViewModel> Consent { get; } = new();
    public ObservableCollection<TweakRowViewModel> Experimental { get; } = new();
    public ObservableCollection<TweakRowViewModel> Excluded { get; } = new();
    public ObservableCollection<StartupSuggestion> StartupSuggestions { get; } = new();
    public ObservableCollection<Finding> Guidance { get; } = new();
    public ObservableCollection<string> ResultLines { get; } = new();

    public OptimizationViewModel(ShellViewModel shell)
    {
        _shell = shell;
        CreateRestorePoint = shell.Services.Settings.CreateRestorePoint;
    }

    public void OnDiagnostic(DiagnosticResult r)
    {
        LightCount = PlanBuilder.Build(ProfileLevel.Light, r.Evaluations).Items.Count(i => i.Section == PlanSection.Standard);
        BalancedCount = PlanBuilder.Build(ProfileLevel.Balanced, r.Evaluations).Items.Count(i => i.Section == PlanSection.Standard);
        MaximumCount = PlanBuilder.Build(ProfileLevel.Maximum, r.Evaluations).Items.Count;
        if (SelectedProfile is { } p) Prepare(p.ToString());
    }

    public void OnEvaluationsRefreshed(DiagnosticResult r) => OnDiagnostic(r);

    [RelayCommand]
    private void Prepare(string profile)
    {
        var r = _shell.Diagnostic;
        if (r == null) return;
        var level = Enum.Parse<ProfileLevel>(profile);
        SelectedProfile = level;
        var plan = PlanBuilder.Build(level, r.Evaluations);
        Standard.Clear(); Consent.Clear(); Experimental.Clear(); Excluded.Clear(); StartupSuggestions.Clear(); Guidance.Clear();
        foreach (var i in plan.Items)
        {
            var row = new TweakRowViewModel(i.Tweak, i.Evaluation) { IsSelected = i.SelectedByDefault, Section = i.Section };
            (i.Section switch { PlanSection.Consent => Consent, PlanSection.Experimental => Experimental, _ => Standard }).Add(row);
        }
        foreach (var e in plan.Excluded) Excluded.Add(new TweakRowViewModel(e.Tweak, e));
        AlreadyApplied = plan.AlreadyApplied.Count;
        NeedsElevation = plan.NeedsElevation;

        // Startup: helpers/updaters/launchers that can be disabled without uninstalling anything (reversible).
        foreach (var e in r.Startup.Where(e => e.Enabled && e.CanToggle && !e.Protected && e.Kind is StartupKind.RunKey or StartupKind.StartupFolder or StartupKind.ScheduledTask
                                                && e.Classification is StartupClassification.Optional or StartupClassification.Risky))
            StartupSuggestions.Add(new StartupSuggestion { Entry = e, IsSelected = e.Category is "updater" or "adware" || e.Classification == StartupClassification.Risky });

        // Guidance that needs the user (BIOS, hardware tuning) — never automatic.
        foreach (var f in r.Findings.Where(f => f.Category is "Bios" or "Ram" or "Display" && f.Severity >= Severity.Suggestion)) Guidance.Add(f);
        if (level == ProfileLevel.Maximum)
            Guidance.Add(new Finding { Id = "guide.oc", Category = "Gpu", Severity = Severity.Info, Title = Loc.T("opt.ocCard.title"), Detail = Loc.T("opt.ocCard.detail"), ActionId = "nav:overclock", ActionLabel = Loc.T("action.openOverclock") });
        HasPlan = true;
        ResultText = "";
        ResultLines.Clear();
        ExperimentalAcknowledged = false;
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (SelectedProfile is not { } level) return;
        var selected = Standard.Concat(Consent).Where(x => x.IsSelected).ToList();
        var experimental = Experimental.Where(x => x.IsSelected).ToList();
        if (experimental.Count > 0 && !ExperimentalAcknowledged)
        {
            Dialogs.Info(Loc.T("opt.experimental.title"), Loc.T("opt.experimental.needAck"));
            return;
        }
        selected.AddRange(experimental);
        var startup = StartupSuggestions.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0 && startup.Count == 0) { Dialogs.Info(Loc.T("opt.nothing.title"), Loc.T("opt.nothing.detail")); return; }

        var bullets = selected.Select(s => $"{s.Name} — {Loc.T("confidence." + s.Confidence)}{(s.NeedsRestart ? " · " + Loc.T("restart." + s.Restart) : "")}")
                              .Concat(startup.Select(s => Loc.T("opt.startupBullet", s.Name))).ToList();
        if (!Dialogs.Confirm(Loc.T("opt.confirm.title", Loc.T("profile." + level)), Loc.T("opt.confirm.detail"), Loc.T("common.apply"), false, bullets)) return;

        _shell.IsBusy = true;
        try
        {
            var progress = new Progress<string>(s => _shell.BusyText = s);
            var title = new LocalizedText($"Profil {Loc.Instance.Get("profile." + level)}", $"{level} profile");
            var result = await _shell.TweakService.ApplyAsync(selected.Select(s => s.Tweak).ToList(), title, level, CreateRestorePoint, progress);
            var controller = new StartupController(_shell.Services.Engine);
            foreach (var s in startup)
            {
                var r = await Task.Run(() => controller.SetEnabled(s.Entry, false));
                result.Lines.Add((r.Success ? "✓ " : "✗ ") + Loc.T("opt.startupBullet", s.Name) + (r.Success ? "" : " — " + r.Error));
                if (r.Success) result.Applied++; else result.Failed++;
            }
            ResultText = TweakService.Summary(result);
            ResultLines.Clear();
            foreach (var l in result.Lines) ResultLines.Add(l);
            await _shell.RefreshEvaluationsAsync();
            Prepare(level.ToString());
            ResultText = TweakService.Summary(result);
            foreach (var l in result.Lines) ResultLines.Add(l);
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        finally { _shell.IsBusy = false; _shell.BusyText = ""; }
    }

    [RelayCommand] private void SelectAll() { foreach (var r in Standard) r.IsSelected = true; }
    [RelayCommand] private void SelectNone() { foreach (var r in Standard.Concat(Consent).Concat(Experimental)) r.IsSelected = false; }
}
