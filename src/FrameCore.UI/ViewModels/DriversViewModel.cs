using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.Core.Elevation;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.DriverManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed partial class DriverUpdateRow : ObservableObject
{
    public DriverUpdate Update { get; init; } = null!;
    [ObservableProperty] private bool _isSelected;
    public string Title => Update.Title;
    public string Detail => $"{Update.DriverManufacturer} · {Update.DriverClass} · {Update.DriverDate:d}";
}

public sealed partial class DriversViewModel : ObservableObject, IDiagnosticConsumer
{
    private readonly ShellViewModel _shell;
    private List<DriverEntry> _all = new();

    [ObservableProperty] private string _category = "All";
    [ObservableProperty] private bool _searching;
    [ObservableProperty] private string _wuStatus = "";
    [ObservableProperty] private DriverEntry? _selected;

    public ObservableCollection<DriverEntry> Drivers { get; } = new();
    public ObservableCollection<DriverUpdateRow> Updates { get; } = new();
    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<LinkInfo> OfficialSources { get; } = new();
    /// <summary>Driver versions seen changing between diagnostics on this PC.</summary>
    public ObservableCollection<string> History { get; } = new();

    public DriversViewModel(ShellViewModel shell) => _shell = shell;

    public void OnDiagnostic(DiagnosticResult r)
    {
        History.Clear();
        try
        {
            foreach (var c in DriverHistory.Record(r.Drivers).Take(30))
                History.Add(Loc.T("drivers.history.line", c.DetectedUtc.ToLocalTime().ToString("g"), c.Device, c.OldVersion ?? "?", c.NewVersion ?? "?"));
        }
        catch (Exception ex) { Core.Log.Warn("Drivers", ex.Message); }
        _all = r.Drivers;
        Filter();
        Findings.Clear();
        foreach (var f in r.Findings.Where(f => f.Category == "Drivers")) Findings.Add(f);
        OfficialSources.Clear();
        foreach (var s in _all.Where(d => d.OfficialUrl != null && d.Category is "Gpu" or "Chipset" or "Audio" or "Ethernet" or "WiFi")
                     .Select(d => new LinkInfo($"{Loc.T("drivers.cat." + d.Category)} — {d.OfficialSource}", d.OfficialUrl!)).DistinctBy(l => l.Label))
            OfficialSources.Add(s);
    }

    partial void OnCategoryChanged(string value) => Filter();

    private void Filter()
    {
        Drivers.Clear();
        foreach (var d in _all.Where(d => Category == "All" ? d.Category != "Other" : d.Category == Category)) Drivers.Add(d);
    }

    [RelayCommand]
    private async Task SearchWindowsUpdate()
    {
        Searching = true;
        WuStatus = Loc.T("drivers.wu.searching");
        Updates.Clear();
        try
        {
            var list = await Task.Run(WindowsUpdateDrivers.Search);
            foreach (var u in list) Updates.Add(new DriverUpdateRow { Update = u });
            WuStatus = list.Count == 0 ? Loc.T("drivers.wu.none") : Loc.T("drivers.wu.found", list.Count);
        }
        catch (Exception ex) { WuStatus = Loc.T("drivers.wu.error", ex.Message); }
        finally { Searching = false; }
    }

    [RelayCommand]
    private async Task InstallSelected()
    {
        var selected = Updates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0) return;
        if (!Dialogs.Confirm(Loc.T("drivers.install.title"), Loc.T("drivers.install.confirm"), Loc.T("drivers.install.ok"), false, selected.Select(s => s.Title))) return;
        _shell.IsBusy = true;
        _shell.BusyText = Loc.T("drivers.install.progress");
        try
        {
            var r = await Task.Run(() => _shell.Privileged.InstallDriverUpdates(selected.Select(s => s.Update.UpdateId).ToList()));
            Dialogs.Info(Loc.T("drivers.install.title"), r.Success ? Loc.T("drivers.install.done") + (r.RebootRequired ? "\n" + Loc.T("apply.reboot") : "") : Loc.T("drivers.install.failed"), r.Messages);
        }
        catch (ElevationDeclinedException) { Dialogs.Info(Loc.T("drivers.install.title"), Loc.T("elevation.declined")); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        finally { _shell.IsBusy = false; _shell.BusyText = ""; }
    }

    [RelayCommand] private void OpenUri(string uri) => Dialogs.OpenUri(uri);
    [RelayCommand] private void OpenDeviceManager() => Dialogs.OpenUri("devmgmt.msc");
    [RelayCommand] private void OpenRollbackHelp() => Dialogs.Info(Loc.T("drivers.rollback.title"), Loc.T("drivers.rollback.detail"));
}
