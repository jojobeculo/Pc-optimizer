using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FrameCore.Core;

/// <summary>Well-known folders. User data lives in %LOCALAPPDATA%\FrameCore (no roaming, no server).</summary>
public static class AppPaths
{
    private static string? _overrideRoot;

    /// <summary>Tests redirect all user data to a temporary folder.</summary>
    public static void OverrideRoot(string root) => _overrideRoot = root;

    public static string Root => _overrideRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameCore");

    public static string Backups => Ensure(Path.Combine(Root, "Backups"));
    public static string Logs => Ensure(Path.Combine(Root, "Logs"));
    public static string Benchmarks => Ensure(Path.Combine(Root, "Benchmarks"));
    public static string Reports => Ensure(Path.Combine(Root, "Reports"));
    public static string Profiles => Ensure(Path.Combine(Root, "Profiles"));
    public static string SettingsFile => Path.Combine(Ensure(Root), "settings.json");
    public static string InstallDir => AppContext.BaseDirectory;
    public static string CatalogDir => Path.Combine(InstallDir, "catalog");
    public static string LangDir => Path.Combine(InstallDir, "lang");

    private static string Ensure(string p) { Directory.CreateDirectory(p); return p; }
}

public static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static string ExecutablePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
    public static Architecture OsArchitecture => RuntimeInformation.OSArchitecture;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "fr";
    public string Theme { get; set; } = "Dark";
    public bool CreateRestorePoint { get; set; } = true;
    public bool CheckForUpdates { get; set; }
    public string? UpdateManifestUrl { get; set; }
    public string? PresentMonPath { get; set; }
    public bool AnonymizeReports { get; set; } = true;
    public bool AdvancedSensors { get; set; }
    public bool FirstRunCompleted { get; set; }
    public int MonitoringIntervalMs { get; set; } = 1000;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile)) return Json.Read<AppSettings>(AppPaths.SettingsFile) ?? new();
        }
        catch (Exception ex) { Log.Warn("Settings", "Unreadable settings, defaults used: " + ex.Message); }
        return new AppSettings();
    }

    public void Save() => Json.WriteAtomic(AppPaths.SettingsFile, this);
}
