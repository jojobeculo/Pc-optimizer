using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.UI.ViewModels;

namespace FrameCore.UI.Infrastructure;

/// <summary>
/// Automated UI walk-through used by CI on a disposable Windows runner (<c>FrameCore.exe --ui-tour &lt;dir&gt;</c>):
/// visits every page, saves a PNG of each, records WPF binding errors, timings, memory and idle CPU, dumps the
/// detected hardware profile, then exits. Nothing is applied: dialogs are headless and elevation is declined.
/// </summary>
public static class UiTour
{
    private sealed class BindingErrorListener : TraceListener
    {
        public readonly List<string> Errors = new();
        private string _pending = "";
        public override void Write(string? message) => _pending += message;
        public override void WriteLine(string? message)
        {
            var line = (_pending + message).Trim();
            _pending = "";
            if (line.Length > 0) lock (Errors) if (Errors.Count < 500) Errors.Add(line);
        }
    }

    private static BindingErrorListener? _listener;

    /// <summary>Must be called before any window is created.</summary>
    public static void AttachBindingListener()
    {
        PresentationTraceSources.Refresh();
        _listener = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    public static async Task RunAsync(string dir, ShellViewModel shell, Window window, Stopwatch sinceStart)
    {
        Directory.CreateDirectory(dir);
        var report = new Dictionary<string, object?>
        {
            ["version"] = AppInfo.Version,
            ["os"] = Environment.OSVersion.VersionString,
            ["elevated"] = AppInfo.IsElevated,
            ["firstWindowMs"] = sinceStart.ElapsedMilliseconds,
        };
        var pages = new List<Dictionary<string, object?>>();
        try
        {
            var sw = Stopwatch.StartNew();
            await shell.StartupAsync();
            report["diagnosticMs"] = sw.ElapsedMilliseconds;
            report["readyMs"] = sinceStart.ElapsedMilliseconds;
            DumpJson(Path.Combine(dir, "profile.json"), shell.Diagnostic?.Profile);
            DumpJson(Path.Combine(dir, "findings.json"), shell.Diagnostic?.Findings.Select(f => new { f.Id, f.Category, f.Severity, f.Title, f.Detail, f.Recommendation, f.Context }));
            DumpJson(Path.Combine(dir, "evaluations.json"), shell.Diagnostic?.Evaluations.Select(e => new { e.Tweak.Id, Status = e.Status.ToString(), e.Compatible, e.IncompatibilityReasons, Current = e.CurrentSummary, e.NeedsElevation }));

            foreach (var nav in shell.Navigation.Select(n => n.Key).ToList())
                pages.Add(await Visit(shell, window, dir, nav, () => shell.CurrentKey = nav, scrollShots: 3));

            foreach (var cat in shell.Pro.Categories.Select(c => c.Key).ToList())
                pages.Add(await Visit(shell, window, dir, "pro-" + cat, () => { shell.CurrentKey = "pro"; shell.Pro.SelectedCategory = cat; }, scrollShots: 2));

            foreach (var section in new[] { "cpu", "gpu", "ram", "board", "storage", "display", "network", "windows" })
                pages.Add(await Visit(shell, window, dir, "hardware-" + section, () => { shell.CurrentKey = "hardware"; shell.Hardware.Section = section; }, scrollShots: 2));

            // Responsiveness of the layout at a small window size, light theme and English.
            window.Width = 1100; window.Height = 700;
            pages.Add(await Visit(shell, window, dir, "small-home", () => shell.CurrentKey = "home", 1));
            pages.Add(await Visit(shell, window, dir, "small-optimization", () => shell.CurrentKey = "optimization", 1));
            window.Width = 1440; window.Height = 900;
            App.ApplyTheme("Light");
            pages.Add(await Visit(shell, window, dir, "light-home", () => shell.CurrentKey = "home", 1));
            pages.Add(await Visit(shell, window, dir, "light-optimization", () => shell.CurrentKey = "optimization", 1));
            App.ApplyTheme("Dark");
            Loc.Instance.SetLanguage("en");
            pages.Add(await Visit(shell, window, dir, "en-home", () => shell.CurrentKey = "home", 1));
            Loc.Instance.SetLanguage("fr");

            // Idle cost of the application on the home page (monitoring running).
            shell.CurrentKey = "home";
            await Task.Delay(2000);
            var proc = Process.GetCurrentProcess();
            var cpu0 = proc.TotalProcessorTime; var t0 = Stopwatch.StartNew();
            await Task.Delay(10000);
            proc.Refresh();
            report["idleCpuPercent"] = Math.Round((proc.TotalProcessorTime - cpu0).TotalMilliseconds / t0.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, 3);
            report["workingSetMB"] = Math.Round(proc.WorkingSet64 / 1048576.0, 1);
            report["privateMB"] = Math.Round(proc.PrivateMemorySize64 / 1048576.0, 1);
            report["managedHeapMB"] = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1);
        }
        catch (Exception ex)
        {
            report["tourError"] = ex.ToString();
            Log.Error("UiTour", "Tour failed", ex);
        }
        report["pages"] = pages;
        lock (_listener?.Errors ?? new List<string>())
            report["bindingErrors"] = _listener?.Errors.Distinct().ToList() ?? new List<string>();
        DumpJson(Path.Combine(dir, "report.json"), report);
        try
        {
            foreach (var f in Directory.GetFiles(AppPaths.Logs))
                File.Copy(f, Path.Combine(dir, "log-" + Path.GetFileName(f)), true);
        }
        catch { /* best effort */ }
        Application.Current.Shutdown(0);
    }

    private static async Task<Dictionary<string, object?>> Visit(ShellViewModel shell, Window window, string dir, string name, Action go, int scrollShots)
    {
        var entry = new Dictionary<string, object?> { ["page"] = name };
        try
        {
            var sw = Stopwatch.StartNew();
            go();
            await Settle();
            entry["renderMs"] = sw.ElapsedMilliseconds;
            await Task.Delay(700); // let entrance animations finish
            await Settle();
            var scroller = FindPageScroller(window);
            var shots = 0;
            for (var i = 0; i < scrollShots; i++)
            {
                if (scroller != null) { scroller.ScrollToVerticalOffset(i * scroller.ViewportHeight * 0.9); await Settle(); }
                Save(window, Path.Combine(dir, $"{name}{(i == 0 ? "" : "-" + i)}.png"));
                shots++;
                if (scroller == null || scroller.ScrollableHeight <= (i + 1) * scroller.ViewportHeight * 0.9) break;
            }
            scroller?.ScrollToTop();
            entry["shots"] = shots;
            entry["scrollableHeight"] = scroller?.ScrollableHeight;
        }
        catch (Exception ex) { entry["error"] = ex.Message; }
        return entry;
    }

    private static async Task Settle()
    {
        await Task.Delay(150);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static ScrollViewer? FindPageScroller(DependencyObject root)
    {
        // The largest visible vertical ScrollViewer hosts the page content.
        ScrollViewer? best = null;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is ScrollViewer sv && sv.IsVisible && sv.ViewportHeight > 200 && (best == null || sv.ActualWidth * sv.ActualHeight > best.ActualWidth * best.ActualHeight)) best = sv;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
        return best;
    }

    private static void Save(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var w = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(window);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void DumpJson(string path, object? value)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(value, Json.Options)); }
        catch (Exception ex) { File.WriteAllText(path, "{\"error\":" + JsonSerializer.Serialize(ex.Message) + "}"); }
    }
}
