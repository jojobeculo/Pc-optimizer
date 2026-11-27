using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed class NavItem
{
    public string Key { get; init; } = "";
    public string Icon { get; init; } = "";
    public bool IsHeader { get; init; }
    public string TitleKey => (IsHeader ? "nav.group." : "nav.") + Key;
    public System.Windows.Media.Geometry? Glyph => Application.Current?.TryFindResource("Icon." + Icon) as System.Windows.Media.Geometry;
}

/// <summary>Main window: navigation, global diagnostic, busy state and cross-page actions.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public AppServices Services { get; }
    public DiagnosticService Diagnostics { get; }
    public MonitoringService Monitoring { get; }
    public TweakService TweakService { get; }
    public PrivilegedClient Privileged { get; }

    public HomeViewModel Home { get; }
    public OptimizationViewModel Optimization { get; }
    public HardwareViewModel Hardware { get; }
    public DriversViewModel Drivers { get; }
    public OverclockViewModel Overclock { get; }
    public ProModeViewModel Pro { get; }
    public BenchmarkViewModel Benchmark { get; }
    public SettingsViewModel Settings { get; }
    public GamesViewModel Games { get; }
    public RestoreViewModel Restore { get; }

    public ObservableCollection<NavItem> Navigation { get; } = new()
    {
        new() { Key = "home", Icon = "Home" },
        new() { Key = "performance", IsHeader = true },
        new() { Key = "optimization", Icon = "Optimize" }, new() { Key = "games", Icon = "Games" }, new() { Key = "benchmark", Icon = "Bench" },
        new() { Key = "system", IsHeader = true },
        new() { Key = "hardware", Icon = "Hardware" }, new() { Key = "drivers", Icon = "Drivers" }, new() { Key = "overclock", Icon = "Overclock" },
        new() { Key = "control", IsHeader = true },
        new() { Key = "restore", Icon = "Restore" }, new() { Key = "pro", Icon = "Pro" }, new() { Key = "settings", Icon = "Settings" },
    };

    [ObservableProperty] private string _currentKey = "home";
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _workerConnected;
    [ObservableProperty] private DiagnosticResult? _diagnostic;

    public string Version => "v" + AppInfo.Version;
    public bool ProcessElevated => AppInfo.IsElevated;

    public ShellViewModel(AppServices services)
    {
        Services = services;
        Diagnostics = new DiagnosticService(services);
        Privileged = new PrivilegedClient(services.Broker, services);
        Monitoring = new MonitoringService(Privileged, services.Settings);
        TweakService = new TweakService(services, Privileged, Diagnostics);
        Home = new HomeViewModel(this);
        Optimization = new OptimizationViewModel(this);
        Hardware = new HardwareViewModel(this);
        Drivers = new DriversViewModel(this);
        Overclock = new OverclockViewModel(this);
        Pro = new ProModeViewModel(this);
        Benchmark = new BenchmarkViewModel(this);
        Settings = new SettingsViewModel(this);
        Games = new GamesViewModel(this);
        Restore = new RestoreViewModel(this);
        CurrentPage = Home;
        services.Broker.StateChanged += (_, _) => Ui(() => WorkerConnected = services.Broker.IsConnected || AppInfo.IsElevated);
        WorkerConnected = AppInfo.IsElevated;
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public static void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    partial void OnCurrentKeyChanged(string value)
    {
        CurrentPage = value switch
        {
            "optimization" => Optimization, "hardware" => Hardware, "drivers" => Drivers, "overclock" => Overclock,
            "pro" => Pro, "benchmark" => Benchmark, "settings" => Settings, "games" => Games, "restore" => Restore, _ => Home,
        };
        (CurrentPage as IPageActivated)?.OnActivated();
    }

    public async Task StartupAsync()
    {
        await RunDiagnostic();
        Monitoring.Gpus = Diagnostic?.Profile.Gpus ?? new();
        Monitoring.Start();
    }

    [RelayCommand]
    public async Task RunDiagnostic()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(s => BusyText = s);
            BusyText = Loc.T("scan.starting");
            var r = await Diagnostics.RunAsync(progress);
            Diagnostic = r;
            Monitoring.Gpus = r.Profile.Gpus;
            StatusText = Loc.T("status.diagDone", r.CompletedAt.ToString("t"), r.Findings.Count);
            foreach (var p in new object[] { Home, Optimization, Hardware, Drivers, Overclock, Pro, Benchmark, Settings, Games, Restore })
                (p as IDiagnosticConsumer)?.OnDiagnostic(r);
            if (!Services.Settings.FirstRunCompleted) { Services.Settings.FirstRunCompleted = true; Services.Settings.Save(); }
            _ = r.StartupTask.ContinueWith(_ => Ui(() =>
            {
                if (!ReferenceEquals(Diagnostic, r)) return;
                DiagnosticService.AddStartupFindings(r);
                Pro.OnStartupScanned(r);
                Home.OnDiagnostic(r);
                if (Optimization.HasPlan) Optimization.FillStartupSuggestions(r);
                StatusText = Loc.T("status.diagDone", r.CompletedAt.ToString("t"), r.Findings.Count);
            }), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Log.Error("Shell", "Diagnostic failed", ex);
            StatusText = Loc.T("status.diagFailed", ex.Message);
        }
        finally { IsBusy = false; BusyText = ""; }
    }

    /// <summary>After a language change: rebuild every computed text from the last diagnostic (no rescan).</summary>
    public void RefreshLanguage()
    {
        if (Diagnostic is not { } r) return;
        Diagnostics.RebuildFindings(r);
        foreach (var p in new object[] { Home, Optimization, Hardware, Drivers, Overclock, Pro, Benchmark, Settings, Games, Restore })
            (p as IDiagnosticConsumer)?.OnDiagnostic(r);
        StatusText = Loc.T("status.diagDone", r.CompletedAt.ToString("t"), r.Findings.Count);
    }

    /// <summary>Re-evaluates catalog statuses after changes (fast, no hardware rescan).</summary>
    public async Task RefreshEvaluationsAsync()
    {
        if (Diagnostic == null) return;
        var evals = await Task.Run(() => Diagnostics.EvaluateAll(Diagnostic.Facts));
        Diagnostic.Evaluations = evals;
        foreach (var p in new object[] { Optimization, Pro, Home, Restore, Games })
            (p as IDiagnosticConsumer)?.OnEvaluationsRefreshed(Diagnostic);
    }

    [RelayCommand] private void OpenUri(string? uri) { if (!string.IsNullOrWhiteSpace(uri)) Dialogs.OpenUri(uri); }

    [RelayCommand]
    public void Navigate(string key)
    {
        if (key.StartsWith("pro:", StringComparison.Ordinal))
        {
            // Category keys are matched without case ("pro:startup" → "Startup") so the list shows the selection.
            var wanted = key[4..];
            Pro.SelectedCategory = Pro.Categories.FirstOrDefault(c => string.Equals(c.Key, wanted, StringComparison.OrdinalIgnoreCase))?.Key ?? wanted;
            CurrentKey = "pro";
            return;
        }
        if (key == "bios") { Hardware.Section = "board"; CurrentKey = "hardware"; return; }
        CurrentKey = key;
    }

    /// <summary>Executes the action attached to a finding ("tweak:id", "open:uri", "nav:page").</summary>
    [RelayCommand]
    private async Task FindingAction(Finding? f)
    {
        if (f?.ActionId == null) return;
        var id = f.ActionId;
        if (id.StartsWith("open:", StringComparison.Ordinal)) { Dialogs.OpenUri(id[5..]); return; }
        if (id.StartsWith("nav:", StringComparison.Ordinal)) { Navigate(id[4..]); return; }
        if (id.StartsWith("tweak:", StringComparison.Ordinal))
        {
            var t = Services.Catalog.Find(id[6..]);
            if (t == null) return;
            await ApplySingleAsync(t);
        }
    }

    public async Task ApplySingleAsync(Core.Models.TweakDefinition t)
    {
        var bullets = new List<string> { t.Description.ToString(), Loc.T("tweak.impactLabel") + " " + t.Impact, Loc.T("tweak.regressionsLabel") + " " + t.Regressions };
        if (t.Restart != RestartRequirement.None) bullets.Add(Loc.T("restart." + t.Restart));
        if (!Dialogs.Confirm(t.Name.ToString(), Loc.T("tweak.confirmApply"), Loc.T("common.apply"), t.Confidence is Confidence.Experimental or Confidence.NotRecommended, bullets)) return;
        IsBusy = true;
        try
        {
            var r = await TweakService.ApplyAsync(new[] { t }, t.Name, null, createRestorePoint: false, new Progress<string>(s => BusyText = s));
            Dialogs.Info(t.Name.ToString(), TweakService.Summary(r), r.Lines);
            await RefreshEvaluationsAsync();
        }
        finally { IsBusy = false; BusyText = ""; }
    }
}

public interface IDiagnosticConsumer
{
    void OnDiagnostic(DiagnosticResult r);
    void OnEvaluationsRefreshed(DiagnosticResult r) { }
}

public interface IPageActivated
{
    void OnActivated();
}
