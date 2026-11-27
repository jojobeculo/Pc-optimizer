using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BenchmarkManager.Verification;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed class ReportRow
{
    public VerifyRepairReport Report { get; init; } = null!;
    public string Date => Report.StartedUtc.ToLocalTime().ToString("g");
    public string Summary => string.Join(" ", Report.Decisions);
    public string Lines => string.Join("\n", Report.Items.Select(i => $"{Loc.T("verifyStatus." + i.Status)} — {i.Title} : {i.Explanation}"));
    public int Effective => Report.Effective;
    public int Total => Report.Items.Count;
}

public sealed class InterruptedRow
{
    public ChangeBatch Batch { get; init; } = null!;
    public List<TweakDefinition> Remaining { get; init; } = new();
    public string Title => Batch.Title.ToString();
    public string Detail => Loc.T("restore.interrupted.detail", Batch.CreatedUtc.ToLocalTime().ToString("g"), Remaining.Count);
}

/// <summary>Restore centre: change history, verification reports, interrupted operations and post-reboot checks.</summary>
public sealed partial class RestoreViewModel : ObservableObject, IDiagnosticConsumer, IPageActivated
{
    private readonly ShellViewModel _shell;
    public ProModeViewModel Pro => _shell.Pro;
    public ObservableCollection<ReportRow> Reports { get; } = new();
    public ObservableCollection<InterruptedRow> Interrupted { get; } = new();
    [ObservableProperty] private string _postRebootText = "";
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _verifiedCount;
    [ObservableProperty] private int _notEffectiveCount;
    /// <summary>"Back to before FrameCore": how many changes are still active and since when.</summary>
    [ObservableProperty] private string _originalStateText = "";
    private bool _checkedAfterBoot;

    public RestoreViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.Services.Journal.Changed += (_, _) => ShellViewModel.Ui(RefreshCounts);
    }

    public void OnDiagnostic(DiagnosticResult r)
    {
        if (!_checkedAfterBoot)
        {
            _checkedAfterBoot = true;
            _ = CheckAfterStartupAsync(r);
        }
        RefreshCounts();
    }

    public void OnActivated() { LoadReports(); RefreshCounts(); Pro.OnActivatedForRestore(); }

    /// <summary>Once per start: re-check reboot-dependent changes and look for interrupted operations.</summary>
    private async Task CheckAfterStartupAsync(DiagnosticResult r)
    {
        try
        {
            var results = await Task.Run(() => _shell.TweakService.VerifyAfterReboot(r.Profile.Os.LastBootUtc));
            if (results.Count > 0)
                PostRebootText = Loc.T("restore.postReboot", results.Count(x => x.Active == true), results.Count, results.Count(x => x.Active == false));
            Interrupted.Clear();
            foreach (var (b, rem) in _shell.TweakService.InterruptedBatches()) Interrupted.Add(new InterruptedRow { Batch = b, Remaining = rem });
            _shell.Home.SetPending(Interrupted.Count, PostRebootText);
        }
        catch (Exception ex) { Core.Log.Error("Restore", "Startup checks failed", ex); }
    }

    private void RefreshCounts()
    {
        var active = _shell.Services.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied).ToList();
        ActiveCount = active.Count;
        VerifiedCount = active.Count(c => c.Entries.All(e => e.ApplyVerified == true));
        NotEffectiveCount = active.Count(c => c.Entries.Any(e => e.ApplyVerified == false));
        var oldest = active.Select(c => c.CreatedUtc).DefaultIfEmpty().Min();
        OriginalStateText = active.Count == 0 ? Loc.T("restore.original.none")
            : Loc.T("restore.original.active", active.Count, oldest.ToLocalTime().ToString("D"), Age(oldest));
    }

    private static string Age(DateTime utc)
    {
        var days = (int)(DateTime.UtcNow - utc).TotalDays;
        return days < 1 ? Loc.T("age.today") : days < 60 ? Loc.T("age.days", days) : Loc.T("age.months", days / 30);
    }

    /// <summary>
    /// Lending or giving the PC away: every change undone (original values written back, however old),
    /// then the light Balanced plan is prepared — nothing is applied without the usual confirmation.
    /// </summary>
    [RelayCommand]
    private async Task HandOver()
    {
        if (!Dialogs.Confirm(Loc.T("restore.handover.title"), Loc.T("restore.handover.confirm"), Loc.T("restore.handover.ok"))) return;
        Dialogs.SuppressNextConfirm = true;
        try { await Pro.RestoreAllCommand.ExecuteAsync(null); }
        finally { Dialogs.SuppressNextConfirm = false; }
        RefreshCounts();
        _shell.Navigate("optimization");
        _shell.Optimization.PrepareCommand.Execute("Balanced");
        Dialogs.Info(Loc.T("restore.handover.title"), ActiveCount == 0 ? Loc.T("restore.handover.done") : Loc.T("restore.handover.partial", ActiveCount));
    }

    private void LoadReports()
    {
        Reports.Clear();
        foreach (var r in _shell.Services.VerificationReports.LoadAll().Take(30)) Reports.Add(new ReportRow { Report = r });
    }

    [RelayCommand]
    private async Task Resume(InterruptedRow? row)
    {
        if (row == null) return;
        if (!Dialogs.Confirm(Loc.T("restore.resume.title"), Loc.T("restore.resume.detail", row.Remaining.Count), Loc.T("restore.resume.go"), false, row.Remaining.Select(t => t.Name.ToString()))) return;
        _shell.IsBusy = true;
        try
        {
            var result = await _shell.TweakService.ApplyAsync(row.Remaining, row.Batch.Title, row.Batch.Profile, createRestorePoint: false,
                new Progress<string>(s => _shell.BusyText = s), resume: row.Batch);
            Interrupted.Remove(row);
            _shell.Home.SetPending(Interrupted.Count, PostRebootText);
            Dialogs.Info(Loc.T("restore.resume.title"), TweakService.Summary(result), result.Lines);
            await _shell.RefreshEvaluationsAsync();
            LoadReports();
        }
        finally { _shell.IsBusy = false; _shell.BusyText = ""; }
    }

    [RelayCommand]
    private void Dismiss(InterruptedRow? row)
    {
        if (row == null) return;
        _shell.TweakService.DismissBatch(row.Batch);
        Interrupted.Remove(row);
        _shell.Home.SetPending(Interrupted.Count, PostRebootText);
    }

    /// <summary>Re-reads every active change now (e.g. after a Windows update) and repairs what Windows reverted.</summary>
    [RelayCommand]
    private async Task VerifyNow()
    {
        _shell.IsBusy = true;
        _shell.BusyText = Loc.T("verify.running");
        try
        {
            if (_shell.Services.Journal.ChangeSets.SelectMany(c => c.Entries).Any(e => _shell.Services.Dispatcher.NeedsElevation(e.Action)) && !_shell.Privileged.IsConnected)
            {
                try { await Task.Run(() => _shell.Privileged.Ensure()); }
                catch (Core.Elevation.ElevationDeclinedException) { }
            }
            var sets = _shell.Services.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied).ToList();
            var report = await Task.Run(() => new VerifyRepairService(_shell.Services.Engine, _shell.Services.Catalog.Find).VerifyAndRepair(sets));
            _shell.Services.VerificationReports.Save(report);
            LoadReports();
            RefreshCounts();
            Pro.ReloadChangeSets();
            Dialogs.Info(Loc.T("restore.verify.title"), string.Join(" ", report.Decisions), report.Items.Where(i => i.Status != VerifyStatus.Effective).Select(i => $"{Loc.T("verifyStatus." + i.Status)} — {i.Title}"));
        }
        finally { _shell.IsBusy = false; _shell.BusyText = ""; }
    }
}
