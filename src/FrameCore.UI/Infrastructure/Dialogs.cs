using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FrameCore.Core;
using FrameCore.Core.Localization;

namespace FrameCore.UI.Infrastructure;

/// <summary>Styled modal dialogs built in code (consistent with the app theme).</summary>
public static class Dialogs
{
    private static Window Owner => Application.Current.MainWindow;

    /// <summary>Automated UI tour (CI): dialogs are logged instead of shown; confirmations are declined.</summary>
    public static bool Headless { get; set; }
    /// <summary>CI only (disposable VM): headless confirmations are accepted so the real flows can be exercised.</summary>
    public static bool AutoConfirm { get; set; }

    private static (Window win, StackPanel body, StackPanel buttons) Create(string title, double width = 480)
    {
        var win = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow,
            Background = (Brush)Application.Current.FindResource("Brush.Card"), Foreground = (Brush)Application.Current.FindResource("Brush.Text"),
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Ui"),
        };
        if (Owner is { IsVisible: true } o && !ReferenceEquals(o, win)) win.Owner = o;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("Text.Section") });
        var body = new StackPanel();
        root.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        root.Children.Add(buttons);
        win.Content = root;
        return (win, body, buttons);
    }

    private static Button MakeButton(string text, string style, Action onClick)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(style), Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static bool Confirm(string title, string message, string? okText = null, bool danger = false, IEnumerable<string>? bullets = null)
    {
        if (Headless) { Log.Info("Dialog", $"[headless confirm {(AutoConfirm ? "accepted" : "declined")}] {title}: {message}"); return AutoConfirm; }
        var (win, body, buttons) = Create(title);
        body.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.FindResource("Text.Body") });
        if (bullets != null)
            foreach (var b in bullets.Take(40))
                body.Children.Add(new TextBlock { Text = "• " + b, Style = (Style)Application.Current.FindResource("Text.Muted"), Margin = new Thickness(6, 4, 0, 0) });
        var result = false;
        buttons.Children.Add(MakeButton(Loc.T("common.cancel"), "Button.Secondary", () => win.Close()));
        buttons.Children.Add(MakeButton(okText ?? Loc.T("common.confirm"), danger ? "Button.Danger" : "Button.Primary", () => { result = true; win.Close(); }));
        win.ShowDialog();
        return result;
    }

    public static void Info(string title, string message, IEnumerable<string>? lines = null)
    {
        if (Headless) { Log.Info("Dialog", $"[headless info] {title}: {message}"); return; }
        var (win, body, buttons) = Create(title);
        body.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.FindResource("Text.Body") });
        if (lines != null)
        {
            var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var l in lines.Take(60)) list.Children.Add(new TextBlock { Text = l, Style = (Style)Application.Current.FindResource("Text.Muted"), Margin = new Thickness(0, 2, 0, 0) });
            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        buttons.Children.Add(MakeButton(Loc.T("common.ok"), "Button.Primary", () => win.Close()));
        win.ShowDialog();
    }

    /// <summary>"Keep these display settings?" with automatic revert after <paramref name="seconds"/>.</summary>
    public static bool KeepChanges(string title, string message, int seconds = 15)
    {
        if (Headless) return true;
        var (win, body, buttons) = Create(title);
        var text = new TextBlock { Style = (Style)Application.Current.FindResource("Text.Body") };
        body.Children.Add(text);
        var keep = false;
        var remaining = seconds;
        void Refresh() => text.Text = message + "\n\n" + Loc.T("dialog.revertIn", remaining);
        Refresh();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { remaining--; if (remaining <= 0) { timer.Stop(); win.Close(); } else Refresh(); };
        buttons.Children.Add(MakeButton(Loc.T("dialog.revert"), "Button.Secondary", () => win.Close()));
        buttons.Children.Add(MakeButton(Loc.T("dialog.keep"), "Button.Primary", () => { keep = true; win.Close(); }));
        win.Closed += (_, _) => timer.Stop();
        timer.Start();
        win.ShowDialog();
        return keep;
    }

    public static void OpenUri(string uri)
    {
        if (Headless) { Log.Info("Dialog", "[headless open] " + uri); return; }
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Shell", $"Cannot open {uri}: {ex.Message}"); }
    }

    public static void Error(string message) => Info(Loc.T("common.error"), message);
}
