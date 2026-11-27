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
            if (shell.Diagnostic is { } d0)
            {
                // The diagnostic report a user would share (anonymised, as by default).
                try
                {
                    File.WriteAllText(Path.Combine(dir, "diagnostic-report.html"), Services.ReportService.BuildHtml(d0, shell.Services.Journal, anonymize: true));
                    File.WriteAllText(Path.Combine(dir, "diagnostic-report.json"), Services.ReportService.BuildJson(d0, shell.Services.Journal, anonymize: true));
                }
                catch (Exception ex) { report["reportError"] = ex.ToString(); }
                report["evaluationStatus"] = d0.Evaluations.GroupBy(e => e.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());
                report["unreadableStates"] = d0.Evaluations.SelectMany(e => e.ActionStates.Where(a => !a.Readable).Select(a => e.Tweak.Id + ": " + a.Error)).Distinct().ToList();
            }
            DumpJson(Path.Combine(dir, "evaluations.json"), shell.Diagnostic?.Evaluations.Select(e => new { e.Tweak.Id, Status = e.Status.ToString(), e.Compatible, e.IncompatibilityReasons, Current = e.CurrentSummary, e.NeedsElevation }));

            var a11y = new List<string>();
            var focusCounts = new Dictionary<string, int>();
            foreach (var nav in shell.Navigation.Where(n => !n.IsHeader).Select(n => n.Key).ToList())
            {
                pages.Add(await Visit(shell, window, dir, nav, () => shell.CurrentKey = nav, scrollShots: 3));
                a11y.AddRange(AccessibilityAudit(window, nav));
                focusCounts[nav] = await KeyboardWalk(window);
            }
            report["accessibilityIssues"] = a11y.Distinct().ToList();
            report["keyboardFocusStops"] = focusCounts;

            foreach (var cat in shell.Pro.Categories.Select(c => c.Key).ToList())
                pages.Add(await Visit(shell, window, dir, "pro-" + cat, () => { shell.CurrentKey = "pro"; shell.Pro.SelectedCategory = cat; }, scrollShots: 2));

            foreach (var section in new[] { "cpu", "gpu", "ram", "board", "storage", "display", "network", "windows", "quality" })
                pages.Add(await Visit(shell, window, dir, "hardware-" + section, () => { shell.CurrentKey = "hardware"; shell.Hardware.Section = section; }, scrollShots: 2));

            foreach (var level in new[] { "Balanced", "Performance", "Maximum" })
                pages.Add(await Visit(shell, window, dir, "plan-" + level, () => { shell.CurrentKey = "optimization"; shell.Optimization.PrepareCommand.Execute(level); }, scrollShots: 3));

            foreach (var section in new[] { "quick", "lab", "ingame" })
                pages.Add(await Visit(shell, window, dir, "bench-" + section, () => { shell.CurrentKey = "benchmark"; shell.Benchmark.Section = section; }, scrollShots: 2));

            shell.CurrentKey = "games";
            await WaitUntil(() => shell.Games.Ready, TimeSpan.FromSeconds(60));
            shell.Games.ShowExplorer = true;
            shell.Games.Selected = shell.Games.Rows.FirstOrDefault();
            pages.Add(await Visit(shell, window, dir, "games-explorer", () => { }, scrollShots: 3));
            report["gamesEstimated"] = shell.Games.Rows.Count(r => r.Estimate.Fps != null);
            report["gamesShowcase"] = shell.Games.Showcase.Select(r => $"{r.Name}: {r.FpsText} ({r.RangeText}) {r.LimiterText} / {r.ConfidenceText} / {r.BasisText}").ToList();

            // User-like flows. They change system settings, so they only run on a disposable CI machine.
            if (Environment.GetEnvironmentVariable("FRAMECORE_DISPOSABLE_VM") == "1")
            {
                Dialogs.AutoConfirm = true;
                report["workerRoundTrip"] = await Task.Run(() => WorkerRoundTrip(shell));
                var flow = Stopwatch.StartNew();
                shell.CurrentKey = "benchmark"; shell.Benchmark.Section = "quick";
                shell.Benchmark.Quick.Mode = "express";
                await shell.Benchmark.Quick.RunFromTourAsync();
                report["flowQuickBenchMs"] = flow.ElapsedMilliseconds;
                pages.Add(await Visit(shell, window, dir, "flow-quickbench-result", () => { }, scrollShots: 3));
                report["quickBenchMetrics"] = shell.Benchmark.Quick.Metrics.Select(m => $"{m.Name}: {m.Value} {m.Unit} [{m.Spread}]").ToList();
                report["quickBenchThrottle"] = shell.Benchmark.Quick.ThrottleText;

                flow.Restart();
                shell.CurrentKey = "optimization";
                shell.Optimization.PrepareCommand.Execute("Balanced");
                await Settle();
                shell.Optimization.MeasureBeforeAfter = true;
                await shell.Optimization.ApplyCommand.ExecuteAsync(null);
                report["flowApplyMs"] = flow.ElapsedMilliseconds;
                report["applyResult"] = shell.Optimization.ResultText;
                report["applyVerification"] = shell.Optimization.VerificationText;
                report["applyPerformance"] = shell.Optimization.PerformanceText;
                report["applyLines"] = shell.Optimization.ResultLines.ToList();
                pages.Add(await Visit(shell, window, dir, "flow-apply-outcome", () => { }, scrollShots: 4));

                shell.Games.Ready = false;
                pages.Add(await Visit(shell, window, dir, "flow-games-after", () => shell.CurrentKey = "games", scrollShots: 2));
                await WaitUntil(() => shell.Games.Ready, TimeSpan.FromSeconds(60));
                pages.Add(await Visit(shell, window, dir, "flow-games-after-loaded", () => { }, scrollShots: 2));
                report["gamesShowcaseAfter"] = shell.Games.Showcase.Select(r => $"{r.Name}: {r.FpsText} -> {r.AfterText} ({r.GainText}) {r.GainReason}").ToList();

                // Pro mode: one optimisation applied with its own button, then its whole category restored.
                var single = shell.Diagnostic!.Evaluations.FirstOrDefault(e => e.Compatible && e.Tweak.Reversible && e.Status == Core.Models.TweakStatus.NotApplied
                    && e.Tweak.Restart == Core.Models.RestartRequirement.None && e.Tweak.Confidence is Core.Models.Confidence.Confirmed or Core.Models.Confidence.Probable);
                if (single != null)
                {
                    shell.CurrentKey = "pro"; shell.Pro.SelectedCategory = single.Tweak.Category;
                    await Settle();
                    var row = shell.Pro.Rows.FirstOrDefault(r => r.Tweak.Id == single.Tweak.Id);
                    var proFlow = new Dictionary<string, object?> { ["tweak"] = single.Tweak.Id, ["rowFound"] = row != null, ["canApplyBefore"] = row?.CanApply };
                    if (row != null)
                    {
                        await row.ApplyCommand.ExecuteAsync(null);
                        await WaitUntil(() => shell.Pro.Rows.FirstOrDefault(r => r.Tweak.Id == single.Tweak.Id)?.Status == Core.Models.TweakStatus.Applied, TimeSpan.FromSeconds(20));
                        var after = shell.Pro.Rows.FirstOrDefault(r => r.Tweak.Id == single.Tweak.Id);
                        proFlow["statusAfterApply"] = after?.Status.ToString();
                        proFlow["canRestoreAfterApply"] = after?.CanRestore;
                        pages.Add(await Visit(shell, window, dir, "flow-pro-single", () => { }, scrollShots: 1));
                        await shell.Pro.RestoreCategoryCommand.ExecuteAsync(single.Tweak.Category);
                        await WaitUntil(() => shell.Pro.Rows.FirstOrDefault(r => r.Tweak.Id == single.Tweak.Id)?.Status == Core.Models.TweakStatus.NotApplied, TimeSpan.FromSeconds(20));
                        proFlow["statusAfterCategoryRestore"] = shell.Pro.Rows.FirstOrDefault(r => r.Tweak.Id == single.Tweak.Id)?.Status.ToString();
                        proFlow["activeAfterCategoryRestore"] = shell.Services.Engine.ActiveChangeSets(single.Tweak.Id).Count();
                    }
                    report["flowProSingle"] = proFlow;
                }

                // Startup manager: disable then re-enable one entry that the user is allowed to toggle.
                await WaitUntil(() => shell.Diagnostic!.StartupTask.IsCompleted, TimeSpan.FromSeconds(60));
                await Settle();
                var entry = shell.Pro.StartupRows.FirstOrDefault(r => r.CanToggle && r.Enabled);
                var startupFlow = new Dictionary<string, object?> { ["entries"] = shell.Pro.StartupRows.Count, ["toggleable"] = shell.Pro.StartupRows.Count(r => r.CanToggle) };
                if (entry != null)
                {
                    startupFlow["name"] = entry.Name;
                    await entry.DisableCommand.ExecuteAsync(null);
                    startupFlow["enabledAfterDisable"] = entry.Enabled;
                    await entry.EnableCommand.ExecuteAsync(null);
                    startupFlow["enabledAfterEnable"] = entry.Enabled;
                }
                report["flowStartup"] = startupFlow;

                pages.Add(await Visit(shell, window, dir, "flow-restore-before", () => shell.CurrentKey = "restore", scrollShots: 3));
                await shell.Restore.VerifyNowCommand.ExecuteAsync(null);
                await shell.Pro.RestoreAllCommand.ExecuteAsync(null);
                pages.Add(await Visit(shell, window, dir, "flow-restore-after", () => { shell.CurrentKey = "home"; shell.CurrentKey = "restore"; }, scrollShots: 3));
                report["restoreStatus"] = shell.Pro.BackupStatus;
                report["activeAfterRestore"] = shell.Services.Journal.ChangeSets.Count(c => c.Status == Core.Models.ChangeSetStatus.Applied);

                foreach (var lab in new[] { "system", "inference" })
                {
                    shell.CurrentKey = "benchmark"; shell.Benchmark.Section = "lab";
                    await shell.Benchmark.Quick.LabCommand.ExecuteAsync(lab);
                    pages.Add(await Visit(shell, window, dir, "flow-lab-" + lab, () => { }, scrollShots: 1));
                    report["lab-" + lab] = shell.Benchmark.Quick.LabResults.Select(m => $"{m.Name}: {m.Value} {m.Unit}").Append(shell.Benchmark.Quick.LabStatus).ToList();
                }
                Dialogs.AutoConfirm = false;
            }

            // Games page with data: CI machines have no identifiable GPU, so the layout is also checked with the
            // estimates of a stated reference machine (screenshots named "games-reference-machine").
            shell.Games.UseMachineForLayoutCheck(new BenchmarkManager.Games.EstimatorMachine
            {
                GpuName = "NVIDIA GeForce RTX 4070", CpuName = "AMD Ryzen 7 7800X3D 8-Core Processor", GpuVramGb = 12, RamGb = 32,
                Channels = Core.Hardware.ChannelMode.Dual, Xmp = Core.Hardware.XmpState.LikelyEnabled,
            });
            shell.Games.ShowExplorer = false; shell.Games.Selected = null;
            shell.CurrentKey = "home"; shell.CurrentKey = "games";
            await WaitUntil(() => shell.Games.Ready, TimeSpan.FromSeconds(60));
            pages.Add(await Visit(shell, window, dir, "games-reference-machine", () => { }, scrollShots: 2));
            shell.Games.ShowExplorer = true;
            shell.Games.Selected = shell.Games.Rows.FirstOrDefault(r => r.Estimate.Fps != null);
            pages.Add(await Visit(shell, window, dir, "games-reference-machine-explorer", () => { }, scrollShots: 3));
            report["gamesReferenceMachine"] = shell.Games.Showcase.Select(r => $"{r.Name}: {r.FpsText} ({r.RangeText}) {r.LimiterText} / {r.ConfidenceText} / {r.BasisText}")
                .Append($"estimated: {shell.Games.Rows.Count(r => r.Estimate.Fps != null)}/{shell.Games.Rows.Count}").ToList();
            shell.Games.ShowExplorer = false; shell.Games.Selected = null;
            if (shell.Diagnostic != null) shell.Games.OnDiagnostic(shell.Diagnostic);

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

    /// <summary>
    /// The path every non-administrator user takes: each change that needs rights goes through the elevated worker
    /// (named pipe, request validation against the catalog, JSON round trip of actions and snapshots). The CI
    /// process is already elevated, so this forces that path with a dedicated broker and a throw-away journal,
    /// applies and restores every compatible elevated tweak, and compares the final state with the original.
    /// </summary>
    private static Dictionary<string, object?> WorkerRoundTrip(ShellViewModel shell)
    {
        var result = new Dictionary<string, object?>();
        var sw = Stopwatch.StartNew();
        var journalDir = Path.Combine(Path.GetTempPath(), "framecore-worker-test-" + Guid.NewGuid().ToString("N"));
        var problems = new List<string>();
        int tried = 0, applied = 0, verified = 0, notApplicable = 0, exact = 0;
        try
        {
            using var broker = new Core.Elevation.ElevationBroker();
            var s = shell.Services;
            var remote = new Core.Engine.RoutingDispatcher(s.Executors, () => broker, processIsElevated: false);
            var local = new Core.Engine.LocalDispatcher(s.Executors);
            var engine = new Core.Engine.TweakEngine(remote, new BackupManager.JsonChangeJournal(journalDir));
            broker.EnsureStarted();
            result["connectMs"] = sw.ElapsedMilliseconds;
            result["ping"] = broker.Send<bool>("ping", new { });
            var facts = shell.Diagnostic?.Facts ?? throw new InvalidOperationException("no diagnostic");
            foreach (var t in s.Catalog.Tweaks.Where(t => t.Reversible && NeedsRights(remote, t)))
            {
                if (t.Actions.Any(a => a.Type == "optionalFeature" || a is { Type: "handler", Name: "display.maxRefresh" })) continue;
                if (!engine.Evaluate(t, facts).Compatible) continue;
                tried++;
                var ctx = new Core.Engine.ActionContext(t.Id, "worker-test");
                string Snap(Core.Models.ActionDefinition a)
                {
                    try { return JsonSerializer.Serialize(local.Capture(a, ctx), Json.Canonical); }
                    catch (Exception ex) { return "unreadable:" + ex.GetType().Name; }
                }
                var before = t.Actions.Select(Snap).ToList();
                try
                {
                    var r = engine.Apply(t, facts);
                    if (r.Success) applied++;
                    else if (r.NotApplicable) notApplicable++;
                    else problems.Add($"{t.Id}: apply failed: {r.Error}");
                    if (r.Verified == true) verified++;
                    else if (r.Verified == false) problems.Add($"{t.Id}: not effective after apply");
                    if (r.ChangeSet != null)
                    {
                        var rr = engine.Restore(r.ChangeSet);
                        if (!rr.Success) problems.Add($"{t.Id}: restore failed: {string.Join("; ", rr.Errors)}");
                    }
                }
                catch (Exception ex) { problems.Add($"{t.Id}: {ex.GetType().Name}: {ex.Message}"); }
                if (before.SequenceEqual(t.Actions.Select(Snap))) exact++;
                else problems.Add($"{t.Id}: original state NOT restored");
            }
            result["ok"] = problems.Count == 0 && tried > 0;
        }
        catch (Exception ex) { result["ok"] = false; problems.Add(ex.ToString()); }
        finally { try { Directory.Delete(journalDir, true); } catch { } }
        result["tried"] = tried; result["applied"] = applied; result["verified"] = verified;
        result["notApplicable"] = notApplicable; result["exactlyRestored"] = exact; result["problems"] = problems;
        result["ms"] = sw.ElapsedMilliseconds;
        return result;

        static bool NeedsRights(Core.Engine.RoutingDispatcher d, Core.Models.TweakDefinition t) => t.Actions.Any(d.NeedsElevation);
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

    /// <summary>Buttons and toggles must have an accessible name and a usable size.</summary>
    private static IEnumerable<string> AccessibilityAudit(Window window, string page)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(window);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            // Only what a keyboard or screen-reader user can reach (template parts such as a combo box arrow are not).
            if (d is System.Windows.Controls.Primitives.ButtonBase b && b.IsVisible && b.Focusable)
            {
                var name = System.Windows.Automation.AutomationProperties.GetName(b);
                var text = b.Content as string ?? FindText(b);
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(text) && b.ToolTip == null)
                    yield return $"{page}: {b.GetType().Name} without accessible name";
                if (b.ActualHeight is > 0 and < 22 && b is not CheckBox && b is not RadioButton { Style: null })
                    yield return $"{page}: {b.GetType().Name} '{text ?? name}' smaller than 22 px ({b.ActualHeight:0})";
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }

    private static string? FindText(DependencyObject d)
    {
        if (d is TextBlock t && !string.IsNullOrWhiteSpace(t.Text)) return t.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindText(VisualTreeHelper.GetChild(d, i)) is { } s) return s;
        return null;
    }

    /// <summary>Walks the page with Tab (MoveFocus) and counts distinct focus stops (keyboard reachability).</summary>
    private static async Task<int> KeyboardWalk(Window window)
    {
        window.Activate();
        System.Windows.Input.Keyboard.Focus(window);
        var seen = new HashSet<object>();
        for (var i = 0; i < 80; i++)
        {
            if (System.Windows.Input.Keyboard.FocusedElement is UIElement el)
            {
                if (!seen.Add(el) && i > 5) break;
                el.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            }
            else window.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            await Task.Delay(5);
        }
        return seen.Count;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < timeout) await Task.Delay(250);
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
