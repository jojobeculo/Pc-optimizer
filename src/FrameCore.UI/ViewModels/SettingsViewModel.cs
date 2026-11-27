using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BenchmarkManager;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Localization;
using FrameCore.Core.Platform;
using FrameCore.HardwareDetection;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;
using Microsoft.Win32;

namespace FrameCore.UI.ViewModels;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string? Notes { get; set; }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private AppSettings S => _shell.Services.Settings;

    [ObservableProperty] private string _language;
    [ObservableProperty] private string _theme;
    [ObservableProperty] private bool _createRestorePoint;
    [ObservableProperty] private bool _advancedSensors;
    [ObservableProperty] private bool _anonymizeReports;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private string _updateUrl;
    [ObservableProperty] private string _presentMonPath;
    [ObservableProperty] private string _presentMonStatus = "";
    [ObservableProperty] private string _pawnIoStatus = "";
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private string _reportStatus = "";

    public string Version => AppInfo.Version;
    public string DataFolder => AppPaths.Root;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _language = S.Language;
        _theme = S.Theme;
        _createRestorePoint = S.CreateRestorePoint;
        _advancedSensors = S.AdvancedSensors;
        _anonymizeReports = S.AnonymizeReports;
        _checkForUpdates = S.CheckForUpdates;
        _updateUrl = S.UpdateManifestUrl ?? "";
        _presentMonPath = S.PresentMonPath ?? "";
        PawnIoStatus = SensorService.PawnIoInstalled ? Loc.T("settings.pawnio.installed") : Loc.T("settings.pawnio.missing");
    }

    partial void OnLanguageChanged(string value)
    {
        S.Language = value; S.Save();
        Loc.Instance.SetLanguage(value);
        _shell.RefreshLanguage();
        PawnIoStatus = SensorService.PawnIoInstalled ? Loc.T("settings.pawnio.installed") : Loc.T("settings.pawnio.missing");
        _ = _shell.RunDiagnostic(); // findings are generated in the active language
    }

    partial void OnThemeChanged(string value) { S.Theme = value; S.Save(); App.ApplyTheme(value); }
    partial void OnCreateRestorePointChanged(bool value) { S.CreateRestorePoint = value; S.Save(); _shell.Optimization.CreateRestorePoint = value; }
    partial void OnAnonymizeReportsChanged(bool value) { S.AnonymizeReports = value; S.Save(); }
    partial void OnCheckForUpdatesChanged(bool value) { S.CheckForUpdates = value; S.Save(); }
    partial void OnUpdateUrlChanged(string value) { S.UpdateManifestUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); S.Save(); }

    partial void OnAdvancedSensorsChanged(bool value)
    {
        S.AdvancedSensors = value; S.Save();
        if (!value) return;
        if (!SensorService.PawnIoInstalled) Dialogs.Info(Loc.T("settings.sensors.title"), Loc.T("settings.sensors.noPawnIo"));
        _ = ConnectSensorsAsync();
    }

    /// <summary>Starts the elevated worker off the UI thread (the UAC prompt and the connection can take seconds).</summary>
    private async Task ConnectSensorsAsync()
    {
        try { await Task.Run(() => _shell.Privileged.Ensure()); }
        catch (ElevationDeclinedException) { AdvancedSensors = false; }
        catch (Exception ex) { Log.Error("Settings", "Sensors elevation failed", ex); Dialogs.Error(ex.Message); AdvancedSensors = false; }
    }

    [RelayCommand]
    private void BrowsePresentMon()
    {
        var dlg = new OpenFileDialog { Filter = "PresentMon|PresentMon*.exe|*.exe|*.exe" };
        if (dlg.ShowDialog() != true) return;
        PresentMonPath = dlg.FileName;
        ValidatePresentMon();
    }

    [RelayCommand]
    private void ValidatePresentMon()
    {
        var (ok, reason) = PresentMonRunner.Validate(PresentMonPath);
        PresentMonStatus = ok ? Loc.T("settings.pm.valid") : reason ?? "";
        S.PresentMonPath = ok ? PresentMonPath : null;
        S.Save();
    }

    [RelayCommand] private void OpenPawnIo() => Dialogs.OpenUri("https://pawnio.eu/");
    [RelayCommand] private void OpenPresentMon() => Dialogs.OpenUri("https://game.intel.com/us/intel-presentmon/");
    [RelayCommand] private void OpenLogs() => Dialogs.OpenUri(AppPaths.Logs);
    [RelayCommand] private void OpenData() => Dialogs.OpenUri(AppPaths.Root);
    [RelayCommand] private void OpenDocs() => Dialogs.OpenUri(Path.Combine(AppPaths.InstallDir, "docs"));

    [RelayCommand]
    private void ExportReport(string format)
    {
        var r = _shell.Diagnostic;
        if (r == null) { ReportStatus = Loc.T("settings.report.needDiag"); return; }
        var html = format == "html";
        var dlg = new SaveFileDialog
        {
            FileName = $"FrameCore-rapport-{DateTime.Now:yyyyMMdd-HHmm}.{(html ? "html" : "json")}",
            Filter = html ? "HTML|*.html" : "JSON|*.json",
            InitialDirectory = AppPaths.Reports,
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, html ? ReportService.BuildHtml(r, _shell.Services.Journal, AnonymizeReports) : ReportService.BuildJson(r, _shell.Services.Journal, AnonymizeReports));
        ReportStatus = Loc.T("settings.report.saved", dlg.FileName);
        if (html) Dialogs.OpenUri(dlg.FileName);
    }

    /// <summary>
    /// Optional update check against a user-configured manifest {version, url, sha256}. The installer is only started
    /// if its SHA-256 matches and its Authenticode signature is valid. Disabled by default; no server is contacted otherwise.
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdates()
    {
        if (string.IsNullOrWhiteSpace(UpdateUrl) || !UpdateUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { UpdateStatus = Loc.T("update.needHttps"); return; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FrameCore-Updater/" + AppInfo.Version);
            var m = JsonSerializer.Deserialize<UpdateManifest>(await http.GetStringAsync(UpdateUrl), Json.Options);
            if (m == null || !System.Version.TryParse(m.Version, out var remote) || string.IsNullOrWhiteSpace(m.Sha256)
                || !Uri.TryCreate(m.Url, UriKind.Absolute, out var setupUri) || setupUri.Scheme != Uri.UriSchemeHttps)
            { UpdateStatus = Loc.T("update.badManifest"); return; }
            if (remote <= System.Version.Parse(AppInfo.Version)) { UpdateStatus = Loc.T("update.upToDate"); return; }
            if (!Dialogs.Confirm(Loc.T("update.title"), Loc.T("update.available", m.Version, m.Notes ?? ""), Loc.T("update.download"))) return;
            var file = Path.Combine(Path.GetTempPath(), $"FrameCore-Setup-{m.Version}.exe");
            await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(setupUri));
            var hash = Authenticode.Sha256(file);
            var sig = Authenticode.Verify(file);
            if (!hash.Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(file); UpdateStatus = Loc.T("update.hashMismatch"); return; }
            if (!sig.Valid && !Dialogs.Confirm(Loc.T("update.title"), Loc.T("update.unsignedConfirm"), Loc.T("update.runAnyway"), true))
            { File.Delete(file); UpdateStatus = Loc.T("update.unsigned", sig.Error); return; }
            UpdateStatus = sig.Valid ? Loc.T("update.verified", sig.Subject) : Loc.T("update.hashOnly");
            if (Dialogs.Confirm(Loc.T("update.title"), Loc.T("update.run"))) Dialogs.OpenUri(file);
        }
        catch (Exception ex) { UpdateStatus = ex.Message; }
    }
}
