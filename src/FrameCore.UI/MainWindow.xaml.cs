using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameCore.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        SourceInitialized += (_, _) => ApplyTitleBarTheme(((App)Application.Current).Resources["Brush.Bg0"] is System.Windows.Media.SolidColorBrush b && b.Color.R < 128);
    }

    /// <summary>Never open larger than the screen (1366×768 laptops, 125–150 % scaling): shrink and center.</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width - 16);
        Height = Math.Min(Height, area.Height - 16);
        if (area.Width < 1200 || area.Height < 760) WindowState = WindowState.Maximized;
    }

    /// <summary>Dark/light native title bar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20, Windows 11).</summary>
    public void ApplyTitleBarTheme(bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
        }
        catch { /* older Windows builds: keep default title bar */ }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
