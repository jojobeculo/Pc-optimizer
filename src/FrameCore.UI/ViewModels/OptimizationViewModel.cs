using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameCore.BenchmarkManager.QuickBench;
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
    [ObservableProperty] private int _balancedCount;
    [ObservableProperty] private int _performanceCount;
    [ObservableProperty] private int _maximumCount;
    [ObservableProperty] private bool _measureBeforeAfter = true;
    [ObservableProperty] private bool _applyRunning;
    [ObservableProperty] private string _currentStep = "";
    [ObservableProperty] private bool _currentStepIndeterminate;
    [ObservableProperty] private string _verificationText = "";
    [ObservableProperty] private string _performanceText = "";
    [ObservableProperty] private bool _hasOutcome;

    public ObservableCollection<StepItem> Steps { get; } = new();
    public ObservableCollection<string> VerificationLines { get; } = new();
    public GamesViewModel Games => _shell.Games;

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
        BalancedCount = PlanBuilder.Build(ProfileLevel.Balanced, r.Evaluations).Items.Count(i => i.Section == PlanSection.Standard);
        PerformanceCount = PlanBuilder.Build(ProfileLevel.Performance, r.Evaluations).Items.Count(i => i.Section == PlanSection.Standard);
        MaximumCount = PlanBuilder.Build(ProfileLevel.Maximum, r.Evaluations).Items.Count;
        if (SelectedProfile is { } p) Prepare(p.ToString());
    }

    public void OnEvaluationsRefreshed(DiagnosticResult r) => OnDiagnostic(r);

    /// <summary>Startup: helpers/updaters/launchers that can be disabled without uninstalling anything (reversible).</summary>
    public void FillStartupSuggestions(DiagnosticResult r)
    {
        StartupSuggestions.Clear();
        List<StartupEntry> list;
        lock (r) list = r.Startup.ToList();
        foreach (var e in list.Where(e => e.Enabled && e.CanToggle && !e.Protected && e.Kind is StartupKind.RunKey or StartupKind.StartupFolder or StartupKind.ScheduledTask
                                          && e.Classification is StartupClassification.Optional or StartupClassification.Risky))
            StartupSuggestions.Add(new StartupSuggestion { Entry = e, IsSelected = e.Category == "adware" || e.Classification == StartupClassification.Risky });
    }

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

        FillStartupSuggestions(r);

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
        ApplyRunning = true;
        HasOutcome = false;
        Steps.Clear();
        var measure = MeasureBeforeAfter && _shell.Diagnostic != null;
        if (measure) Steps.Add(new StepItem { Key = "benchBefore", Title = Loc.T("opt.step.benchBefore") });
        if (CreateRestorePoint) Steps.Add(new StepItem { Key = "RestorePoint", Title = Loc.T("opt.step.restorePoint") });
        Steps.Add(new StepItem { Key = "Apply", Title = Loc.T("opt.step.apply") });
        Steps.Add(new StepItem { Key = "Verify", Title = Loc.T("opt.step.verify") });
        if (measure) Steps.Add(new StepItem { Key = "benchAfter", Title = Loc.T("opt.step.benchAfter") });
        void Step(string key, double? fraction)
        {
            var idx = Steps.ToList().FindIndex(s => s.Key == key);
            if (idx < 0) return;
            for (var i = 0; i < idx; i++) if (Steps[i].State != "done") { Steps[i].State = "done"; Steps[i].Fraction = 1; }
            var st = Steps[idx];
            st.State = fraction >= 1 ? "done" : "active";
            st.Fraction = fraction ?? 0;
            CurrentStep = st.Title;
            CurrentStepIndeterminate = fraction == null;
        }
        QuickBenchResult? before = null;
        try
        {
            var progress = new Progress<string>(s => _shell.BusyText = s);
            if (measure)
            {
                Step("benchBefore", 0);
                before = await RunExpressBench(Loc.T("opt.bench.beforeLabel", Loc.T("profile." + level)), f => Step("benchBefore", f));
            }
            var title = new LocalizedText($"Profil {Loc.Instance.Get("profile." + level)}", $"{level} profile");
            var result = await _shell.TweakService.ApplyAsync(selected.Select(s => s.Tweak).ToList(), title, level, CreateRestorePoint, progress,
                (stage, f) => Step(stage switch { ApplyStage.RestorePoint => "RestorePoint", ApplyStage.Verify => "Verify", ApplyStage.Done => "Verify", _ => "Apply" },
                                   stage == ApplyStage.Done ? 1 : stage == ApplyStage.Elevation ? null : f));
            var controller = new StartupController(_shell.Services.Engine);
            foreach (var s in startup)
            {
                var r = await Task.Run(() => controller.SetEnabled(s.Entry, false));
                result.Lines.Add((r.Success ? "✓ " : "✗ ") + Loc.T("opt.startupBullet", s.Name) + (r.Success ? "" : " — " + r.Error));
                if (r.Success) result.Applied++; else result.Failed++;
            }
            // Measure after, compare, and undo only changes behind a confirmed regression (never in a loop).
            if (measure && before != null && !before.Cancelled && result.Applied > 0)
            {
                Step("benchAfter", 0);
                var after = await RunExpressBench(Loc.T("opt.bench.afterLabel", Loc.T("profile." + level)), f => Step("benchAfter", f));
                if (!after.Cancelled && result.Verification != null)
                {
                    var sets = _shell.Services.Journal.ChangeSets.Where(c => c.BatchId == result.BatchId).ToList();
                    var verifier = new FrameCore.BenchmarkManager.Verification.VerifyRepairService(_shell.Services.Engine, _shell.Services.Catalog.Find);
                    var facts = _shell.Diagnostic!.Facts;
                    await Task.Run(() => verifier.CheckPerformance(result.Verification, sets, before, after,
                        comps => RunBenchSync(Loc.T("opt.bench.recheck"), comps), () => facts));
                    _shell.Services.VerificationReports.Save(result.Verification);
                    var cmp = result.Verification.Performance;
                    PerformanceText = cmp?.IndexAfterRelativeToBefore is { } idx ? Loc.T("opt.perf.index", idx.ToString("0.0")) + " " + string.Join(" ", result.Verification.Decisions.Skip(1)) : "";
                }
                Step("benchAfter", 1);
            }
            foreach (var s in Steps) { s.State = "done"; s.Fraction = 1; }
            ResultText = TweakService.Summary(result);
            ResultLines.Clear();
            foreach (var l in result.Lines) ResultLines.Add(l);
            VerificationLines.Clear();
            if (result.Verification != null)
            {
                VerificationText = string.Join(" ", result.Verification.Decisions.Take(1));
                foreach (var i in result.Verification.Items.Where(i => i.Status != FrameCore.BenchmarkManager.Verification.VerifyStatus.Effective))
                    VerificationLines.Add($"{Loc.T("verifyStatus." + i.Status)} — {i.Title} : {i.Explanation}");
            }
            HasOutcome = true;
            await _shell.RefreshEvaluationsAsync();
            _ = _shell.Games.LoadAsync();
            Prepare(level.ToString());
            ResultText = TweakService.Summary(result);
            foreach (var l in result.Lines) ResultLines.Add(l);
        }
        catch (Exception ex) { Core.Log.Error("Optimize", "Apply failed", ex); Dialogs.Error(ex.Message); }
        finally { _shell.IsBusy = false; _shell.BusyText = ""; ApplyRunning = false; CurrentStep = ""; }
    }

    private Task<QuickBenchResult> RunExpressBench(string label, Action<double> onProgress)
    {
        var progress = new Progress<QuickBenchProgress>(p => onProgress(p.OverallFraction));
        var options = QuickBenchFactory.Options(_shell.Services, _shell.Monitoring, _shell.Diagnostic?.Profile, "express");
        return Task.Run(() =>
        {
            var r = new QuickBenchRunner(options).Run(label, progress, CancellationToken.None);
            r.MachineSignature = _shell.Diagnostic?.Profile.Signature ?? "";
            r.ActiveTweakIds = _shell.Services.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied && c.TweakId != null).Select(c => c.TweakId!).Distinct().ToList();
            if (!r.Cancelled) _shell.Services.QuickBenches.Save(r);
            return r;
        });
    }

    private QuickBenchResult RunBenchSync(string label, HashSet<BenchComponent> only)
    {
        var options = QuickBenchFactory.Options(_shell.Services, _shell.Monitoring, _shell.Diagnostic?.Profile, "express", only);
        return new QuickBenchRunner(options).Run(label, null, CancellationToken.None);
    }

    [RelayCommand] private void SelectAll() { foreach (var r in Standard) r.IsSelected = true; }
    [RelayCommand] private void SelectNone() { foreach (var r in Standard.Concat(Consent).Concat(Experimental)) r.IsSelected = false; }
}
