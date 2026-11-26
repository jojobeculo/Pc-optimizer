using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameCore.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTitleBarTheme(((App)Application.Current).Resources["Brush.Bg0"] is System.Windows.Media.SolidColorBrush b && b.Color.R < 128);
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
