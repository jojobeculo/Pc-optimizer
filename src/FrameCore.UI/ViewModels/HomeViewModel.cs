using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed partial class HomeViewModel : ObservableObject, IDiagnosticConsumer
{
    private readonly ShellViewModel _shell;
    public ShellViewModel Shell => _shell;

    [ObservableProperty] private bool _hasDiagnostic;
    [ObservableProperty] private string _signature = "";
    [ObservableProperty] private int _critical;
    [ObservableProperty] private int _warnings;
    [ObservableProperty] private int _suggestions;
    [ObservableProperty] private string _healthTitle = "";
    [ObservableProperty] private string _healthLevel = "Neutral";
    [ObservableProperty] private string _lastOptimization = "";

    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _cpuDetail = "";
    [ObservableProperty] private string _cpuUsage = "–";
    [ObservableProperty] private string _cpuTemp = "";
    [ObservableProperty] private string _cpuClock = "";
    [ObservableProperty] private double[] _cpuHistory = Array.Empty<double>();

    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _gpuDetail = "";
    [ObservableProperty] private string _gpuUsage = "–";
    [ObservableProperty] private string _gpuTemp = "";
    [ObservableProperty] private string _gpuVram = "";
    [ObservableProperty] private double[] _gpuHistory = Array.Empty<double>();

    [ObservableProperty] private string _ramTotal = "";
    [ObservableProperty] private string _ramDetail = "";
    [ObservableProperty] private string _ramUsage = "–";
    [ObservableProperty] private double[] _ramHistory = Array.Empty<double>();

    [ObservableProperty] private string _storageTitle = "";
    [ObservableProperty] private string _storageDetail = "";
    [ObservableProperty] private double _systemFreePercent;
    [ObservableProperty] private string _displayTitle = "";
    [ObservableProperty] private string _displayDetail = "";
    [ObservableProperty] private string _windowsTitle = "";
    [ObservableProperty] private string _windowsDetail = "";

    public ObservableCollection<Finding> Findings { get; } = new();

    public HomeViewModel(ShellViewModel shell)
    {
        _shell = shell;
        shell.Monitoring.Updated += (_, s) => ShellViewModel.Ui(() => OnSample(s));
        shell.Services.Journal.Changed += (_, _) => ShellViewModel.Ui(UpdateLastOptimization);
        UpdateLastOptimization();
    }

    private void UpdateLastOptimization()
    {
        var b = _shell.Services.Journal.Batches.FirstOrDefault();
        var c = _shell.Services.Journal.ChangeSets.FirstOrDefault();
        LastOptimization = b != null ? Loc.T("home.lastOpt", b.Title, b.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), b.ChangeSetIds.Count)
                         : c != null ? Loc.T("home.lastChange", c.Title, c.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                         : Loc.T("home.noOpt");
    }

    public void OnDiagnostic(DiagnosticResult r)
    {
        var p = r.Profile;
        HasDiagnostic = true;
        Signature = p.Signature;
        Critical = r.Count(Severity.Critical);
        Warnings = r.Count(Severity.Warning);
        Suggestions = r.Count(Severity.Suggestion);
        (HealthTitle, HealthLevel) = Critical > 0 ? (Loc.T("home.health.critical"), "Danger")
                                   : Warnings > 0 ? (Loc.T("home.health.warnings"), "Warning")
                                   : Suggestions > 0 ? (Loc.T("home.health.suggestions"), "Accent")
                                   : (Loc.T("home.health.good"), "Success");
        CpuName = p.Cpu.Name;
        CpuDetail = Loc.T("home.cpu.detail", p.Cpu.Cores, p.Cpu.Threads, p.Cpu.Microarchitecture ?? Loc.Unknown);
        var g = p.PrimaryGpu;
        GpuName = g?.Name ?? Loc.Unknown;
        GpuDetail = g == null ? "" : Loc.T("home.gpu.detail", g.DedicatedVramBytes is { } v && v > 0 ? BytesConverter.Format(v) : Loc.Unknown, g.DriverVersion ?? Loc.Unknown);
        RamTotal = BytesConverter.Format(p.Memory.TotalBytes);
        var channels = Loc.T("channels." + p.Memory.Channels);
        if (p.Memory.ChannelsConfidence == DataConfidence.Inferred) channels = Loc.T("channels.probable", channels);
        RamDetail = Loc.T("home.ram.detail", p.Memory.Type ?? Loc.Unknown, p.Memory.ConfiguredSpeedMts?.ToString() ?? Loc.Unknown, channels);
        var sys = p.Storage.SelectMany(s => s.Volumes.Select(v => (s, v))).FirstOrDefault(x => x.v.IsSystem);
        if (sys.v != null)
        {
            StorageTitle = $"{sys.v.Letter}: — {sys.s.FriendlyName}";
            StorageDetail = Loc.T("home.storage.detail", BytesConverter.Format(sys.v.FreeBytes), BytesConverter.Format(sys.v.SizeBytes), Loc.T("media." + sys.s.Media));
            SystemFreePercent = 100 - sys.v.FreePercent;
        }
        var d = p.Displays.FirstOrDefault(x => x.IsPrimary) ?? p.Displays.FirstOrDefault();
        if (d != null)
        {
            DisplayTitle = d.FriendlyName ?? d.DeviceName;
            DisplayDetail = Loc.T("home.display.detail", d.CurrentWidth, d.CurrentHeight, d.CurrentRefreshHz.ToString("0.##"), d.MaxRefreshAtCurrentResolution?.ToString("0") ?? "?");
        }
        WindowsTitle = $"{p.Os.ProductName} {p.Os.DisplayVersion}";
        WindowsDetail = $"Build {p.Os.FullBuild} · {p.Os.ServicingNote}";
        Findings.Clear();
        foreach (var f in r.Findings.Take(12)) Findings.Add(f);
        UpdateLastOptimization();
    }

    public void OnEvaluationsRefreshed(DiagnosticResult r) => UpdateLastOptimization();

    private void OnSample(MonitorSnapshot s)
    {
        CpuUsage = s.Cpu.TotalPercent is { } c ? $"{c:0} %" : "–";
        CpuTemp = s.CpuTemp is { } t ? $"{t:0} °C" : Loc.T("home.tempUnknown");
        CpuClock = s.Cpu.EffectiveMhz is { } mhz ? $"{mhz / 1000:0.00} GHz" : "";
        CpuHistory = _shell.Monitoring.CpuHistory.Snapshot();
        var g = s.PrimaryGpu;
        GpuUsage = g?.UtilizationPercent is { } gu ? $"{gu:0} %" : "–";
        GpuTemp = (g?.TemperatureC ?? s.GpuTemp) is { } gt ? $"{gt:0} °C" : Loc.T("home.tempUnknown");
        GpuVram = g?.DedicatedUsedBytes is { } used ? $"{BytesConverter.Format(used)} / {(g.DedicatedTotalBytes is { } tot ? BytesConverter.Format(tot) : "?")}" : "";
        GpuHistory = _shell.Monitoring.GpuHistory.Snapshot();
        RamUsage = $"{s.Memory.UsedPercent:0} %";
        RamHistory = _shell.Monitoring.RamHistory.Snapshot();
    }
}
