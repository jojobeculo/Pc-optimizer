using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BenchmarkManager;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.CpuManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;
using Microsoft.Win32;

namespace FrameCore.UI.ViewModels;

public sealed class ProcessChoice
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string Display => $"{Name}  —  {Title}  (PID {Pid})";
}

public sealed class SessionRow
{
    public BenchmarkSession Session { get; init; } = null!;
    public string Title => $"{Session.Label} — {Session.Target}";
    public string Detail => Session.Method == CaptureMethod.Synthetic
        ? string.Join(" · ", Session.Scores.Select(kv => $"{Loc.T("bench.score." + kv.Key)} {kv.Value:0.##}"))
        : Loc.T("bench.session.detail", Session.Mean(s => s.AverageFps).ToString("0.0"), Session.Mean(s => s.Low1PercentFps).ToString("0.0"), Session.Runs.Count, Session.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
}

public sealed partial class BenchmarkViewModel : ObservableObject, IDiagnosticConsumer, IPageActivated
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private ProcessChoice? _selectedProcess;
    [ObservableProperty] private string _labelKind = "before";
    [ObservableProperty] private string _customLabel = "";
    [ObservableProperty] private int _durationSeconds = 60;
    [ObservableProperty] private int _runs = 3;
    [ObservableProperty] private string _method = "etw";
    [ObservableProperty] private bool _running;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private SessionRow? _selectedSession;
    [ObservableProperty] private SessionRow? _sessionA;
    [ObservableProperty] private SessionRow? _sessionB;
    [ObservableProperty] private double[] _frameTimes = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _historySeries;
    [ObservableProperty] private IReadOnlyList<string>? _historyLabels;
    [ObservableProperty] private string _presentMonState = "";

    public ObservableCollection<ProcessChoice> Processes { get; } = new();
    public ObservableCollection<SessionRow> Sessions { get; } = new();
    public ObservableCollection<PropertyRow> SelectedStats { get; } = new();
    public ObservableCollection<MetricComparison> Comparison { get; } = new();
    public ObservableCollection<Finding> Bottlenecks { get; } = new();

    /// <summary>quick | lab | ingame | history</summary>
    [ObservableProperty] private string _section = "quick";
    public QuickBenchViewModel Quick { get; }
    /// <summary>Optional: the library game being captured, so the measurement calibrates its FPS estimate.</summary>
    public ObservableCollection<FrameCore.BenchmarkManager.Games.GameInfo> GameChoices { get; } = new();
    [ObservableProperty] private FrameCore.BenchmarkManager.Games.GameInfo? _captureGame;
    [ObservableProperty] private string _capturePreset = "High";

    public BenchmarkViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Quick = new QuickBenchViewModel(shell);
        LoadSessions();
    }

    public void OnDiagnostic(DiagnosticResult r) { }
    public void OnActivated()
    {
        Quick.Load();
        if (GameChoices.Count == 0)
            try { foreach (var g in _shell.Services.Games.Games.OrderBy(g => g.Name, StringComparer.CurrentCulture)) GameChoices.Add(g); }
            catch (Exception ex) { Log.Warn("Bench", "game library: " + ex.Message); }
        RefreshProcesses();
        var pm = PresentMonRunner.Validate(_shell.Services.Settings.PresentMonPath);
        PresentMonState = pm.ok ? Loc.T("bench.pm.ready") : Loc.T("bench.pm.missing");
    }

    [RelayCommand]
    private void RefreshProcesses()
    {
        Processes.Clear();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle) || p.Id == Environment.ProcessId) continue;
                    Processes.Add(new ProcessChoice { Pid = p.Id, Name = p.ProcessName, Title = p.MainWindowTitle });
                }
                catch { }
            }
        }
    }

    private string LabelText() => LabelKind == "custom" && !string.IsNullOrWhiteSpace(CustomLabel) ? CustomLabel : Loc.T("bench.label." + LabelKind);

    [RelayCommand]
    private async Task Start()
    {
        if (SelectedProcess is not { } target) { Dialogs.Info(Loc.T("bench.title"), Loc.T("bench.selectProcess")); return; }
        if (Method == "presentmon" && !PresentMonRunner.Validate(_shell.Services.Settings.PresentMonPath).ok) { Dialogs.Info(Loc.T("bench.title"), Loc.T("bench.pm.missing")); return; }
        try { await Task.Run(() => _shell.Privileged.Ensure()); }
        catch (ElevationDeclinedException) { Dialogs.Info(Loc.T("bench.title"), Loc.T("bench.needsAdmin")); return; }

        Running = true;
        _cts = new CancellationTokenSource();
        var p = _shell.Diagnostic?.Profile;
        var display = p?.Displays.FirstOrDefault(d => d.IsPrimary);
        var session = new BenchmarkSession
        {
            Label = LabelText(), Kind = LabelKind, Target = target.Name, Method = Method == "presentmon" ? CaptureMethod.PresentMon : CaptureMethod.Etw,
            GameId = CaptureGame?.Id, Width = display?.CurrentWidth, Height = display?.CurrentHeight, Preset = CaptureGame != null ? CapturePreset : null,
            Conditions = new BenchmarkConditions
            {
                Resolution = display != null ? $"{display.CurrentWidth}x{display.CurrentHeight}" : null, RefreshHz = display?.CurrentRefreshHz,
                PowerPlan = SafePowerPlan(), GpuDriver = p?.PrimaryGpu?.DriverVersion,
                AppliedProfile = _shell.Services.Journal.Batches.FirstOrDefault()?.Title.ToString(),
            },
        };
        try
        {
            for (var run = 1; run <= Runs && !_cts.IsCancellationRequested; run++)
            {
                if (run > 1 && !Dialogs.Confirm(Loc.T("bench.nextRun.title", run, Runs), Loc.T("bench.nextRun.detail"), Loc.T("bench.nextRun.go"))) break;
                Status = Loc.T("bench.capturing", run, Runs, target.Name);
                var timeline = new List<SensorPoint>();
                var started = DateTime.Now;
                void OnSample(object? _, MonitorSnapshot s) => timeline.Add(new SensorPoint
                {
                    T = (s.Time - started).TotalSeconds, Cpu = s.Cpu.TotalPercent, CpuMaxCore = s.Cpu.MaxCorePercent, Gpu = s.PrimaryGpu?.UtilizationPercent,
                    CpuTemp = s.CpuTemp, GpuTemp = s.PrimaryGpu?.TemperatureC ?? s.GpuTemp, CpuMhz = s.Cpu.EffectiveMhz, GpuMhz = s.PrimaryGpu?.CoreClockMhz,
                    RamUsedGb = s.Memory.UsedBytes / 1073741824.0, VramUsedGb = s.PrimaryGpu?.DedicatedUsedBytes / 1073741824.0, PerfLimit = s.Cpu.PerformanceLimitPercent,
                });
                _shell.Monitoring.Updated += OnSample;
                var progressTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                progressTimer.Tick += (_, _) => Progress = Math.Min(100, (DateTime.Now - started).TotalSeconds / DurationSeconds * 100);
                progressTimer.Start();
                CaptureResult cap;
                try
                {
                    var req = new CaptureRequest { ProcessId = target.Pid, ProcessName = target.Name, DurationSeconds = DurationSeconds };
                    cap = await Task.Run(() => Method == "presentmon" ? _shell.Privileged.CapturePresentMon(_shell.Services.Settings.PresentMonPath!, req) : _shell.Privileged.CaptureEtw(req));
                }
                finally { _shell.Monitoring.Updated -= OnSample; progressTimer.Stop(); }
                if (cap.Error != null) { Status = cap.Error; break; }
                var stats = FrameStatistics.Compute(cap.FrameTimesMs);
                session.Runs.Add(new BenchmarkRun { Stats = stats, Timeline = timeline, Sensors = Summarize(timeline, p), FrameTimesMs = cap.FrameTimesMs.Take(20000).ToList() });
            }
            if (session.Runs.Count > 0)
            {
                _shell.Services.Benchmarks.Save(session);
                LoadSessions();
                SelectedSession = Sessions.FirstOrDefault(s => s.Session.Id == session.Id);
                Status = Loc.T("bench.done", session.Runs.Count);
            }
        }
        catch (Exception ex) { Status = ex.Message; Log.Error("Bench", "capture", ex); }
        finally { Running = false; Progress = 0; }
    }

    private string? SafePowerPlan()
    {
        try
        {
            var g = _shell.Services.Power.GetActiveScheme();
            return _shell.Services.Power.ListSchemes().FirstOrDefault(s => s.Guid == g)?.Name;
        }
        catch { return null; }
    }

    private static SensorSummary Summarize(List<SensorPoint> t, Core.Hardware.MachineProfile? p)
    {
        double? Avg(Func<SensorPoint, double?> f) { var v = t.Select(f).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Average() : null; }
        double? Max(Func<SensorPoint, double?> f) { var v = t.Select(f).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Max() : null; }
        return new SensorSummary
        {
            CpuAvgPercent = Avg(x => x.Cpu), CpuMaxCorePercent = Avg(x => x.CpuMaxCore), CpuEffectiveMhz = Avg(x => x.CpuMhz), CpuTempMaxC = Max(x => x.CpuTemp),
            CpuPerfLimitPercent = Avg(x => x.PerfLimit), GpuAvgPercent = Avg(x => x.Gpu), GpuTempMaxC = Max(x => x.GpuTemp), GpuClockMhz = Avg(x => x.GpuMhz),
            VramUsedGb = Max(x => x.VramUsedGb), VramTotalGb = p?.PrimaryGpu?.DedicatedVramBytes / 1073741824.0, RamUsedGb = Max(x => x.RamUsedGb),
            RamTotalGb = p == null ? null : p.Memory.TotalBytes / 1073741824.0,
        };
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ImportCsv()
    {
        var dlg = new OpenFileDialog { Filter = "PresentMon CSV|*.csv" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var caps = PresentMonCsvParser.ParseFile(dlg.FileName);
            var main = caps.FirstOrDefault() ?? throw new FormatException(Loc.T("bench.etw.noFrames"));
            var session = new BenchmarkSession
            {
                Label = LabelText(), Kind = LabelKind, Target = main.Application, Method = CaptureMethod.CsvImport,
                Conditions = new BenchmarkConditions { Notes = Path.GetFileName(dlg.FileName) },
                Runs = { new BenchmarkRun { Stats = FrameStatistics.Compute(main.FrameTimesMs), FrameTimesMs = main.FrameTimesMs.Take(20000).ToList() } },
            };
            _shell.Services.Benchmarks.Save(session);
            LoadSessions();
            SelectedSession = Sessions.FirstOrDefault(s => s.Session.Id == session.Id);
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task RunSynthetic()
    {
        Running = true;
        try
        {
            Status = Loc.T("bench.synthetic.running");
            var session = new BenchmarkSession { Label = LabelText(), Kind = LabelKind, Target = Loc.T("bench.synthetic.target"), Method = CaptureMethod.Synthetic };
            var (single, multi) = await Task.Run(() => CpuStressTest.Benchmark(TimeSpan.FromSeconds(10)));
            session.Scores["cpu.single"] = single;
            session.Scores["cpu.multi"] = multi;
            session.Scores["mem.bandwidth"] = await Task.Run(SyntheticBenchmarks.MemoryBandwidthGBs);
            session.Scores["mem.latency"] = await Task.Run(SyntheticBenchmarks.MemoryLatencyNs);
            _shell.Services.Benchmarks.Save(session);
            LoadSessions();
            SelectedSession = Sessions.FirstOrDefault(s => s.Session.Id == session.Id);
            Status = Loc.T("bench.synthetic.done");
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { Running = false; }
    }

    private void LoadSessions()
    {
        Sessions.Clear();
        foreach (var s in _shell.Services.Benchmarks.LoadAll()) Sessions.Add(new SessionRow { Session = s });
    }

    partial void OnSelectedSessionChanged(SessionRow? value)
    {
        SelectedStats.Clear(); Bottlenecks.Clear(); FrameTimes = Array.Empty<double>();
        if (value == null) return;
        var s = value.Session;
        if (s.Method == CaptureMethod.Synthetic)
        {
            foreach (var kv in s.Scores) SelectedStats.Add(new PropertyRow(Loc.T("bench.score." + kv.Key), kv.Value.ToString("0.##", CultureInfo.CurrentCulture)));
        }
        else
        {
            string M(Func<FrameStats, double> f) => s.Runs.Count > 1 ? $"{s.Mean(f):0.0} ± {s.StdDev(f):0.0}" : $"{s.Mean(f):0.0}";
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.avgFps"), M(x => x.AverageFps)));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.low1"), M(x => x.Low1PercentFps)));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.low01"), s.Runs.All(r => r.Stats.Low01PercentFps != null) && s.Runs.Count > 0 ? M(x => x.Low01PercentFps ?? 0) : Loc.T("bench.low01.unreliable")));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.avgFt"), M(x => x.AverageFrameTimeMs) + " ms"));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.p95"), M(x => x.P95FrameTimeMs) + " ms"));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.p99"), M(x => x.P99FrameTimeMs) + " ms"));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.stddev"), M(x => x.FrameTimeStdDevMs) + " ms"));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.stutters"), string.Join(" / ", s.Runs.Select(r => r.Stats.Stutters))));
            SelectedStats.Add(new PropertyRow(Loc.T("bench.metric.frames"), string.Join(" / ", s.Runs.Select(r => r.Stats.Frames))));
            var first = s.Runs.FirstOrDefault();
            if (first?.Sensors is { } ss)
            {
                SelectedStats.Add(new PropertyRow(Loc.T("bench.sensor.cpu"), ss.CpuAvgPercent is { } c ? $"{c:0} % (max core {ss.CpuMaxCorePercent:0} %, {ss.CpuEffectiveMhz:0} MHz, {ss.CpuTempMaxC?.ToString("0") ?? "?"} °C)" : null));
                SelectedStats.Add(new PropertyRow(Loc.T("bench.sensor.gpu"), ss.GpuAvgPercent is { } g ? $"{g:0} % ({ss.GpuClockMhz?.ToString("0") ?? "?"} MHz, {ss.GpuTempMaxC?.ToString("0") ?? "?"} °C)" : null));
                SelectedStats.Add(new PropertyRow(Loc.T("bench.sensor.mem"), $"RAM {ss.RamUsedGb:0.0}/{ss.RamTotalGb:0.0} Go · VRAM {ss.VramUsedGb?.ToString("0.0") ?? "?"}/{ss.VramTotalGb?.ToString("0.0") ?? "?"} Go"));
            }
            SelectedStats.Add(new PropertyRow(Loc.T("bench.conditions"), $"{s.Conditions.Resolution} @ {s.Conditions.RefreshHz:0} Hz · {s.Conditions.PowerPlan} · {Loc.T("bench.driver")} {s.Conditions.GpuDriver} · {s.Method}"));
            FrameTimes = first?.FrameTimesMs?.TakeLast(1500).ToArray() ?? Array.Empty<double>();
            if (first != null)
                foreach (var f in BottleneckAnalyzer.Analyze(new BottleneckInput
                {
                    Target = s.Target, DurationSeconds = first.Stats.DurationSeconds, Resolution = s.Conditions.Resolution, RefreshHz = s.Conditions.RefreshHz,
                    Frames = first.Stats, Samples = first.Timeline, RamTotalGb = first.Sensors.RamTotalGb, VramTotalGb = first.Sensors.VramTotalGb,
                    GpuLimitReasons = first.Sensors.GpuLimitReasons,
                })) Bottlenecks.Add(f);
        }
        UpdateHistory(s.Target);
    }

    private void UpdateHistory(string target)
    {
        var same = Sessions.Select(r => r.Session).Where(s => s.Target == target && s.Method != CaptureMethod.Synthetic).OrderBy(s => s.CreatedUtc).ToList();
        if (same.Count == 0) { HistorySeries = null; return; }
        var b1 = (Brush)Application.Current.FindResource("Brush.Chart1");
        var b2 = (Brush)Application.Current.FindResource("Brush.Chart2");
        HistorySeries = new List<ChartSeries>
        {
            new() { Name = Loc.T("bench.metric.avgFps"), Brush = b1, Values = same.Select(s => s.Mean(x => x.AverageFps)).ToList() },
            new() { Name = Loc.T("bench.metric.low1"), Brush = b2, Values = same.Select(s => s.Mean(x => x.Low1PercentFps)).ToList() },
        };
        HistoryLabels = same.Select(s => s.Label.Length > 12 ? s.Label[..12] : s.Label).ToList();
    }

    [RelayCommand] private void SetA() { SessionA = SelectedSession; Compare(); }
    [RelayCommand] private void SetB() { SessionB = SelectedSession; Compare(); }

    private void Compare()
    {
        Comparison.Clear();
        if (SessionA == null || SessionB == null || SessionA.Session.Method == CaptureMethod.Synthetic || SessionB.Session.Method == CaptureMethod.Synthetic) return;
        foreach (var c in SessionComparer.Compare(SessionA.Session, SessionB.Session)) Comparison.Add(c);
    }

    [RelayCommand]
    private void DeleteSession()
    {
        if (SelectedSession == null || !Dialogs.Confirm(Loc.T("bench.delete.title"), SelectedSession.Title, Loc.T("common.delete"), true)) return;
        _shell.Services.Benchmarks.Delete(SelectedSession.Session);
        LoadSessions();
    }

    [RelayCommand] private void OpenPresentMonPage() => Dialogs.OpenUri("https://game.intel.com/us/intel-presentmon/");
}
