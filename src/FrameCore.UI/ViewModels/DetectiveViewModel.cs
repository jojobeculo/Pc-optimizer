using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BenchmarkManager.Detective;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed partial class FindingRow : ObservableObject
{
    public DetectiveFinding Finding { get; init; } = null!;
    public string Title => Finding.Title;
    public string Evidence => Finding.Evidence;
    public string Advice => Finding.Advice;
    public string AreaText => Loc.T("detective.area." + Finding.Area);
    public string SeverityKey => Finding.Severity switch { Severity.Critical => "Danger", Severity.Warning => "Warning", Severity.Suggestion => "Accent", _ => "Info" };
    public string SeverityText => Loc.T("severity." + Finding.Severity);
    public bool CanFix => Finding.Fix != DetectiveFix.None;
    public string FixLabel => Loc.T("detective.fixLabel." + Finding.Fix);
    public bool HasEvidence => Evidence.Length > 0;
    public bool HasAdvice => Advice.Length > 0;
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _fixed;
}

public sealed class AreaCard
{
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "Icon.Search";
    public int Count { get; init; }
    public string WorstKey { get; init; } = "Success";
    public string Summary { get; init; } = "";
}

public sealed class ProcessRow
{
    public ProcessVerdict V { get; init; } = null!;
    public string Name => V.Instances > 1 ? $"{V.Name}  ×{V.Instances}" : V.Name;
    public string TierKey => V.Tier switch
    {
        ProcessTier.Harmful => "Danger", ProcessTier.Useless => "Warning", ProcessTier.LowValue => "Info", ProcessTier.Useful => "Neutral",
        ProcessTier.VeryUseful => "Accent", _ => "Success",
    };
    public string TierText => Loc.T("tier." + V.Tier);
    public string Reason => V.Reason;
    public string Cpu => V.CpuPercent.ToString(V.CpuPercent < 0.1 ? "0.00" : "0.0", CultureInfo.CurrentCulture) + " %";
    public string Wakeups => V.WakeupsPerSec.ToString("N0", CultureInfo.CurrentCulture);
    public string Memory => V.MemoryMb.ToString("N0", CultureInfo.CurrentCulture) + " Mo";
    public double WakeFraction { get; init; }
    public string ActionText { get; init; } = "";
    public bool Protected => V.Protected;
}

public sealed class DriverRow
{
    public string Driver { get; init; } = "";
    public string Component { get; init; } = "";
    public string Max { get; init; } = "";
    public double Fraction { get; init; }
    public string Detail { get; init; } = "";
    public string SeverityKey { get; init; } = "Info";
}

/// <summary>"Detective" page: consented full investigation, findings with their evidence, one-click fixes.</summary>
public sealed partial class DetectiveViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _cts;
    private DetectiveReport? _report;

    public ObservableCollection<StepItem> Steps { get; } = new();
    public ObservableCollection<FindingRow> Findings { get; } = new();
    public ObservableCollection<AreaCard> Areas { get; } = new();
    public ObservableCollection<ProcessRow> Processes { get; } = new();
    public ObservableCollection<DriverRow> Drivers { get; } = new();
    public ObservableCollection<StabilityEvent> StabilityEvents { get; } = new();

    [ObservableProperty] private bool _running;
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private bool _deep = true;
    [ObservableProperty] private string _level = "Maximum";
    [ObservableProperty] private double _overall;
    [ObservableProperty] private string _currentStep = "";
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _suggestionCount;
    [ObservableProperty] private int _fixableCount;
    [ObservableProperty] private string _reportTitle = "";
    [ObservableProperty] private string _tierFilter = "all";
    [ObservableProperty] private string _processSummary = "";
    [ObservableProperty] private string _latencySummary = "";
    [ObservableProperty] private string _networkSummary = "";
    [ObservableProperty] private IReadOnlyList<double>? _gatewayPings;
    [ObservableProperty] private IReadOnlyList<double>? _internetPings;
    [ObservableProperty] private double _pingMax = 50;
    [ObservableProperty] private string _fixAllResult = "";

    public DetectiveViewModel(ShellViewModel shell)
    {
        _shell = shell;
        if (shell.Detective.Last is { } last) Show(last);
    }

    private ProfileLevel LevelValue => Enum.TryParse<ProfileLevel>(Level, out var l) ? l : ProfileLevel.Maximum;
    partial void OnTierFilterChanged(string value) { if (_report != null) FillProcesses(_report); }
    partial void OnLevelChanged(string value) { if (_report != null) FillProcesses(_report); }

    /// <summary>Asks once for every permission the investigation needs, listing exactly what it reads.</summary>
    public static bool AskConsent(bool deep) =>
        Dialogs.Confirm(Loc.T("detective.consent.title"), Loc.T("detective.consent.detail", deep ? Loc.T("detective.duration.deep") : Loc.T("detective.duration.quick")),
            Loc.T("detective.consent.ok"), false,
            new[] { "consent.processes", "consent.latency", "consent.network", "consent.logs", "consent.storage", "consent.history", "consent.nothingSent", "consent.nothingChanged" }
                .Select(k => Loc.T("detective." + k)));

    [RelayCommand]
    private async Task Start()
    {
        if (Running) return;
        if (_shell.Diagnostic == null) { Dialogs.Info(Loc.T("detective.title"), Loc.T("quick.needDiagnostic")); return; }
        if (!AskConsent(Deep)) return;
        await RunAsync(Deep, LevelValue);
    }

    /// <summary>Runs the investigation (also used by the "complete" optimisation mode, consent already given).</summary>
    public async Task<DetectiveReport?> RunAsync(bool deep, ProfileLevel level)
    {
        if (Running) return null;
        Running = true; Overall = 0; FixAllResult = "";
        _cts = new CancellationTokenSource();
        Steps.Clear();
        foreach (var k in DetectiveService.StepKeys) Steps.Add(new StepItem { Key = k, Title = Loc.T("detective.step." + k) });
        try { await Task.Run(() => _shell.Privileged.Ensure()); } catch { /* declined: the parts that need rights say so */ }
        void Progress(string key, double fraction, string detail) => ShellViewModel.Ui(() =>
        {
            var idx = Steps.ToList().FindIndex(s => s.Key == key);
            if (idx < 0) return;
            var s = Steps[idx];
            s.Fraction = fraction; s.State = fraction >= 1 ? "done" : "active"; if (detail.Length > 0) s.Detail = detail;
            if (fraction < 1) CurrentStep = s.Title + (detail.Length > 0 ? " — " + detail : "");
            Overall = Steps.Average(x => x.State == "done" ? 1 : x.Fraction);
        });
        try
        {
            var r = await _shell.Detective.RunAsync(deep, level, Progress, _cts.Token);
            Show(r);
            _shell.StatusText = Loc.T("detective.done", r.Findings.Count(f => f.Severity >= Severity.Suggestion));
            return r;
        }
        catch (Exception ex) { Core.Log.Error("Detective", "Investigation failed", ex); Dialogs.Error(ex.Message); return null; }
        finally { Running = false; CurrentStep = ""; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    public void Show(DetectiveReport r)
    {
        _report = r;
        Findings.Clear();
        foreach (var f in r.Findings) Findings.Add(new FindingRow { Finding = f });
        CriticalCount = r.Count(Severity.Critical); WarningCount = r.Count(Severity.Warning); SuggestionCount = r.Count(Severity.Suggestion);
        FixableCount = r.Findings.Count(f => f.AutoFixable);
        ReportTitle = Loc.T("detective.reportTitle", r.StartedUtc.ToLocalTime().ToString("g"), Math.Max(1, (r.FinishedUtc - r.StartedUtc).TotalSeconds).ToString("0"));
        Areas.Clear();
        foreach (var area in Enum.GetValues<DetectiveArea>())
        {
            var list = r.Findings.Where(f => f.Area == area).ToList();
            if (list.Count == 0) continue;
            var worst = list.Max(f => f.Severity);
            var issues = list.Count(f => f.Severity >= Severity.Suggestion);
            Areas.Add(new AreaCard
            {
                Title = Loc.T("detective.area." + area), Icon = "Icon." + AreaIcon(area), Count = issues,
                WorstKey = issues == 0 ? "Success" : worst switch { Severity.Critical => "Danger", Severity.Warning => "Warning", _ => "Accent" },
                Summary = issues == 0 ? Loc.T("detective.area.ok") : Loc.T("detective.area.issues", issues),
            });
        }
        Drivers.Clear();
        if (r.Latency is { Measured: true } lat)
        {
            var max = Math.Max(0.5, lat.Drivers.Select(d => d.MaxMs).DefaultIfEmpty().Max());
            foreach (var d in lat.Drivers.Take(10))
                Drivers.Add(new DriverRow
                {
                    Driver = d.Driver, Component = Loc.T("detective.component." + d.Component), Max = d.MaxMs.ToString("0.000", CultureInfo.CurrentCulture) + " ms",
                    Fraction = d.MaxMs / max, SeverityKey = LatencyProbe.Verdict(d.MaxMs) switch { Severity.Critical => "Danger", Severity.Warning => "Warning", Severity.Suggestion => "Accent", _ => "Success" },
                    Detail = Loc.T("detective.lat.row", d.Count.ToString("N0"), d.P99Ms.ToString("0.000"), d.TotalMs.ToString("0")),
                });
            LatencySummary = Loc.T("detective.lat.summary", Math.Max(lat.MaxDpcMs, lat.MaxIsrMs).ToString("0.000"), lat.BusyPercent.ToString("0.00"), lat.Seconds.ToString("0"));
        }
        else LatencySummary = r.Latency?.Error ?? "";
        if (r.Network is { } n)
        {
            GatewayPings = n.Gateway?.Samples.ToArray(); InternetPings = n.Internet?.Samples.ToArray();
            PingMax = Math.Max(10, (n.Internet?.MaxMs ?? 0) * 1.2);
            NetworkSummary = Loc.T("detective.net.summary", n.Adapter ?? "?", n.Wired ? Loc.T("detective.net.wired") : Loc.T("detective.net.wireless"), n.LinkMbps,
                n.Gateway?.AvgMs.ToString("0.0") ?? "—", n.Internet?.AvgMs.ToString("0.0") ?? "—", n.Internet?.JitterMs.ToString("0.0") ?? "—");
        }
        StabilityEvents.Clear();
        foreach (var e in r.Stability.Where(e => e.Kind != "appCrash").Take(15)) StabilityEvents.Add(e);
        FillProcesses(r);
        HasReport = true;
    }

    private static string AreaIcon(DetectiveArea a) => a switch
    {
        DetectiveArea.Latency => "Timer", DetectiveArea.Network => "Network", DetectiveArea.Stability => "Shield", DetectiveArea.Hardware => "Chip",
        DetectiveArea.Processes => "Layers", DetectiveArea.Storage => "Disk", DetectiveArea.Stutter => "Activity", _ => "TrendDown",
    };

    private void FillProcesses(DetectiveReport r)
    {
        Processes.Clear();
        var level = LevelValue;
        var list = r.Processes.Where(p => TierFilter == "all" || p.Tier.ToString() == TierFilter).ToList();
        var max = Math.Max(1, r.Processes.Select(p => p.WakeupsPerSec).DefaultIfEmpty().Max());
        foreach (var p in list.Take(150))
            Processes.Add(new ProcessRow
            {
                V = p, WakeFraction = p.WakeupsPerSec / max,
                ActionText = Loc.T("procAction." + ProcessClassifier.ActionFor(p, level)),
            });
        var counts = Enum.GetValues<ProcessTier>().Select(t => $"{Loc.T("tier." + t)} {r.Processes.Where(p => p.Tier == t).Sum(p => p.Instances)}");
        ProcessSummary = string.Join("  ·  ", counts);
    }

    [RelayCommand]
    private async Task Fix(FindingRow? row)
    {
        if (row == null || _report == null || !row.CanFix) return;
        if (row.Finding.AutoFixable && !Dialogs.Confirm(Loc.T("detective.fix.title"), row.Title, row.FixLabel, false, new[] { row.Advice })) return;
        row.Result = await new DetectiveFixer(_shell).ApplyAsync(row.Finding, _report);
        row.Fixed = row.Finding.AutoFixable;
    }

    /// <summary>Every automatic fix at once, listed and confirmed first.</summary>
    [RelayCommand]
    private async Task FixAll()
    {
        if (_report == null) return;
        var rows = Findings.Where(f => f.Finding.AutoFixable && !f.Fixed).ToList();
        if (rows.Count == 0) { Dialogs.Info(Loc.T("detective.fix.title"), Loc.T("detective.fix.none")); return; }
        if (!Dialogs.Confirm(Loc.T("detective.fix.allTitle"), Loc.T("detective.fix.allDetail"), Loc.T("common.apply"), false, rows.Select(r => $"{r.FixLabel} — {r.Title}"))) return;
        var fixer = new DetectiveFixer(_shell);
        foreach (var r in rows) { r.Result = await fixer.ApplyAsync(r.Finding, _report); r.Fixed = true; }
        FixAllResult = Loc.T("detective.fix.allDone", rows.Count);
    }

    /// <summary>Process actions of the chosen level (used by the complete optimisation): listed, confirmed, applied.</summary>
    public List<(ProcessVerdict V, ProcessAction A)> PlannedProcessActions(ProfileLevel level) =>
        _report?.Processes.Select(p => (p, ProcessClassifier.ActionFor(p, level))).Where(x => x.Item2 != ProcessAction.None).ToList() ?? new();

    [RelayCommand]
    private async Task LoadedTest()
    {
        if (_report?.Network == null) return;
        if (!Dialogs.Confirm(Loc.T("detective.bloat.title"), Loc.T("detective.bloat.detail"), Loc.T("detective.bloat.start"))) return;
        var n = _report.Network;
        var gw = _shell.Diagnostic?.Profile.Network.FirstOrDefault(a => a.IsUp && a.IsPhysical && a.Gateway != null)?.Gateway;
        var a = gw != null ? NetworkProbe.PingAsync(gw, 150, 100, CancellationToken.None) : Task.FromResult<PingSummary>(null!);
        var b = NetworkProbe.PingAsync("1.1.1.1", 150, 100, CancellationToken.None);
        n.GatewayLoaded = await a; n.InternetLoaded = await b;
        _report.Findings.RemoveAll(f => f.Id == "net.bufferbloat");
        _report.Findings.AddRange(NetworkProbe.Analyze(n, _shell.Diagnostic?.Profile.Network ?? new()).Where(f => f.Id == "net.bufferbloat"));
        _shell.Detective.Store.Save(_report);
        Show(_report);
    }
}
