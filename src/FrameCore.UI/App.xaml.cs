using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;
using FrameCore.UI.ViewModels;

namespace FrameCore.UI;

public partial class App : Application
{
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Elevated worker mode: no UI at all.
        if (e.Args.Length >= 3 && e.Args[0] == "--worker")
        {
            Loc.Instance.LoadDirectory(AppPaths.LangDir);
            var code = int.TryParse(e.Args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? WorkerProgram.Run(e.Args[1], pid) : 2;
            Shutdown(code);
            return;
        }

        base.OnStartup(e);
        var startClock = System.Diagnostics.Stopwatch.StartNew();
        var tourDir = e.Args.Length >= 2 && e.Args[0] == "--ui-tour" ? e.Args[1] : null;
        if (tourDir != null) { Dialogs.Headless = true; UiTour.AttachBindingListener(); }
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("App", "Unhandled", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("App", "Unobserved task", ex.Exception); ex.SetObserved(); };

        var settings = AppSettings.Load();
        Loc.Instance.LoadDirectory(AppPaths.LangDir);
        Loc.Instance.SetLanguage(settings.Language);
        ApplyTheme(settings.Theme);
        Log.Info("App", $"FrameCore {AppInfo.Version} starting (elevated={AppInfo.IsElevated}, os={Environment.OSVersion.Version})");

        var services = new AppServices(settings);
        services.Broker.ConfirmElevation = () => Dispatcher.Invoke(() =>
            Dialogs.Confirm(Loc.T("elevation.title"), Loc.T("elevation.explain"), Loc.T("elevation.continue")));
        _shell = new ShellViewModel(services);
        var window = new MainWindow { DataContext = _shell };
        SyncWindowLanguage(window);
        Loc.Instance.LanguageChanged += (_, _) => SyncWindowLanguage(window);
        if (tourDir != null) { window.Width = 1440; window.Height = 900; window.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        MainWindow = window;
        window.Show();
        if (tourDir != null) _ = UiTour.RunAsync(tourDir, _shell, window, startClock);
        else _ = _shell.StartupAsync();
    }

    /// <summary>Bindings with StringFormat (dates, decimals) use the element language: follow the UI culture.</summary>
    private static void SyncWindowLanguage(FrameworkElement root) =>
        root.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

    public static void ApplyTheme(string theme)
    {
        var dicts = Current.Resources.MergedDictionaries;
        var colors = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Colors.", StringComparison.Ordinal) == true);
        var next = new ResourceDictionary { Source = new Uri(theme == "Light" ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml", UriKind.Relative) };
        if (colors != null) dicts[dicts.IndexOf(colors)] = next; else dicts.Insert(0, next);
        if (Current.MainWindow is MainWindow mw) mw.ApplyTitleBarTheme(theme != "Light");
    }

    /// <summary>Number of unexpected UI exceptions since start (reported by the automated tour).</summary>
    public static int UiExceptionCount;
    private string? _lastErrorShown;
    private DateTime _lastErrorShownAt;

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Interlocked.Increment(ref UiExceptionCount);
        e.Handled = true;
        // A layout error can repeat on every pass: log it, but show the same message at most once a minute.
        var message = e.Exception.Message;
        var repeated = message == _lastErrorShown && DateTime.UtcNow - _lastErrorShownAt < TimeSpan.FromMinutes(1);
        if (repeated) return;
        Log.Error("App", "UI exception", e.Exception);
        _lastErrorShown = message; _lastErrorShownAt = DateTime.UtcNow;
        try { Dialogs.Error(Loc.T("app.unexpectedError", message)); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _shell?.Monitoring.Dispose();
            _shell?.Services.Broker.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
