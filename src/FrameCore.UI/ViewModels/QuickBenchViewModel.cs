using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.CpuManager;
using FrameCore.GpuManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

/// <summary>One step of a long operation (shown by the step progress indicator). Fraction is real or null (unknown).</summary>
public sealed partial class StepItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>pending | active | done | failed | skipped</summary>
    [ObservableProperty] private string _state = "pending";
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _detail = "";
}

public sealed class MetricRow
{
    public string Name { get; init; } = "";
    public string Component { get; init; } = "";
    public string Value { get; init; } = "";
    public string Spread { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Note { get; init; } = "";
}

public sealed class ComparisonRow
{
    public string Name { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string Absolute { get; init; } = "";
    public string Percent { get; init; } = "";
    public string Verdict { get; init; } = "";
    /// <summary>Improved | Regressed | WithinNoise | Inconclusive | NotComparable — drives the colour.</summary>
    public string VerdictKey { get; init; } = "";
    public string Confidence { get; init; } = "";
}

public sealed class QuickRunRow
{
    public QuickBenchResult Result { get; init; } = null!;
    public string Title => $"{Result.StartedUtc.ToLocalTime():g} · {Result.Label}";
    public string Detail => Loc.T("quick.history.detail", Result.Metrics.Count(m => m.Measured), Math.Round(Result.Duration.TotalMinutes, 1), Result.ActiveTweakIds.Count);
}

/// <summary>Quick benchmark (~5 min), before/after comparison and the benchmark lab.</summary>
public sealed partial class QuickBenchViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _cts;

    public ObservableCollection<StepItem> Steps { get; } = new();
    public ObservableCollection<MetricRow> Metrics { get; } = new();
    public ObservableCollection<ComparisonRow> Comparison { get; } = new();
    public ObservableCollection<QuickRunRow> History { get; } = new();
    public ObservableCollection<MetricRow> LabResults { get; } = new();

    [ObservableProperty] private string _mode = "standard";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _running;
    [ObservableProperty] private double _overall;
    [ObservableProperty] private string _currentStep = "";
    [ObservableProperty] private string _thermalText = "";
    [ObservableProperty] private string _throttleText = "";
    [ObservableProperty] private string _throttleKey = "Unknown";
    [ObservableProperty] private string _indexText = "";
    [ObservableProperty] private double _indexValue = double.NaN;
    [ObservableProperty] private string _comparisonTitle = "";
    [ObservableProperty] private QuickRunRow? _compareBefore;
    [ObservableProperty] private QuickRunRow? _compareAfter;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _hasComparison;
    [ObservableProperty] private string _labStatus = "";
    [ObservableProperty] private bool _networkConsent;
    [ObservableProperty] private int _stressMinutes = 5;

    public QuickBenchViewModel(ShellViewModel shell) => _shell = shell;

    public void Load()
    {
        History.Clear();
        foreach (var r in _shell.Services.QuickBenches.LoadAll().Take(40)) History.Add(new QuickRunRow { Result = r });
        if (History.Count > 0 && !HasResult) Show(History[0].Result);
        if (History.Count >= 2 && CompareAfter == null) { CompareAfter = History[0]; CompareBefore = History[1]; }
    }

    partial void OnCompareBeforeChanged(QuickRunRow? value) => Compare();
    partial void OnCompareAfterChanged(QuickRunRow? value) => Compare();

    private static readonly string[] StepKeys = { "cpu.single", "cpu.multi", "cpu.sustained", "mem.bandwidth", "mem.latency", "disk", "gpu", "sys.wake" };

    [RelayCommand]
    private async Task Start()
    {
        if (Running) return;
        if (_shell.Diagnostic == null) { Dialogs.Info(Loc.T("quick.title"), Loc.T("quick.needDiagnostic")); return; }
        if (!Dialogs.Confirm(Loc.T("quick.confirm.title"), Loc.T(Mode == "express" ? "quick.confirm.express" : "quick.confirm.standard"), Loc.T("quick.start"))) return;
        Running = true;
        _cts = new CancellationTokenSource();
        Steps.Clear();
        foreach (var k in StepKeys) Steps.Add(new StepItem { Key = k, Title = Loc.T("quick.step." + k) });
        Overall = 0;
        var label = string.IsNullOrWhiteSpace(Label) ? Loc.T("quick.defaultLabel", DateTime.Now.ToString("g")) : Label.Trim();
        var options = QuickBenchFactory.Options(_shell.Services, _shell.Monitoring, _shell.Diagnostic.Profile, Mode);
        var progress = new Progress<QuickBenchProgress>(p =>
        {
            Overall = p.OverallFraction;
            for (var i = 0; i < Steps.Count; i++)
            {
                var s = Steps[i];
                if (s.Key == p.StepKey) { s.State = p.StepFraction >= 1 ? "done" : "active"; s.Fraction = p.StepFraction; CurrentStep = s.Title; }
                else if (Array.IndexOf(StepKeys, s.Key) < Array.IndexOf(StepKeys, p.StepKey) && s.State != "done") { s.State = "done"; s.Fraction = 1; }
            }
        });
        try
        {
            var result = await Task.Run(() => new QuickBenchRunner(options).Run(label, progress, _cts.Token));
            result.MachineSignature = _shell.Diagnostic.Profile.Signature;
            result.GpuDriver = _shell.Diagnostic.Profile.PrimaryGpu?.DriverVersion;
            result.OsBuild = _shell.Diagnostic.Profile.Os.Build * 100000 + _shell.Diagnostic.Profile.Os.Ubr;
            result.ActiveTweakIds = _shell.Services.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied && c.TweakId != null).Select(c => c.TweakId!).Distinct().ToList();
            foreach (var s in Steps.Where(s => s.State != "done")) s.State = result.Cancelled ? "skipped" : "done";
            if (!result.Cancelled) _shell.Services.QuickBenches.Save(result);
            Show(result);
            Load();
            if (History.Count >= 2) { CompareAfter = History[0]; CompareBefore = History[1]; }
            _shell.StatusText = Loc.T(result.Cancelled ? "quick.cancelled" : "quick.done");
            _shell.Games.OnEvaluationsRefreshed(_shell.Diagnostic);
        }
        catch (Exception ex)
        {
            Core.Log.Error("QuickBench", "Run failed", ex);
            Dialogs.Error(ex.Message);
        }
        finally { Running = false; CurrentStep = ""; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    /// <summary>Lets the automated UI tour run the benchmark through the same code path as the Start button.</summary>
    public Task RunFromTourAsync() => Start();

    private void Show(QuickBenchResult r)
    {
        Metrics.Clear();
        foreach (var m in r.Metrics) Metrics.Add(ToRow(m));
        var t = r.Thermals;
        ThermalText = t.Samples == 0 ? Loc.T("quick.thermal.none")
            : Loc.T("quick.thermal", Fmt(t.CpuTempMaxC, "0"), Fmt(t.GpuTempMaxC, "0"), Fmt(t.CpuClockAvgMhz, "0"), Fmt(t.CpuClockMinUnderLoadMhz, "0"), Fmt(t.PerformanceLimitMinPercent, "0"));
        ThrottleKey = r.Throttling.ToString();
        ThrottleText = Loc.T("throttle." + r.Throttling, r.ThrottlingDetail ?? "");
        HasResult = true;
    }

    public static MetricRow ToRow(BenchMetric m)
    {
        var (lo, hi) = m.Ci95;
        return new MetricRow
        {
            Name = Loc.T("metric." + m.Key), Component = Loc.T("bench.component." + m.Component), Unit = m.Unit,
            Value = m.Measured ? m.Median.ToString(m.Median >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture) : "—",
            Spread = !m.Measured ? Loc.T("quick.unavailable." + (m.UnavailableReason ?? "unknown"))
                   : m.Samples.Count < 2 ? Loc.T("quick.singleSample")
                   : Loc.T("quick.spread", m.Samples.Count, m.CvPercent.ToString("0.0"), double.IsNaN(lo) ? "—" : lo.ToString("N1"), double.IsNaN(hi) ? "—" : hi.ToString("N1")),
            Note = m.HigherIsBetter ? Loc.T("quick.higherBetter") : Loc.T("quick.lowerBetter"),
        };
    }

    private static string Fmt(double? v, string f) => v is { } x ? x.ToString(f, CultureInfo.CurrentCulture) : "—";

    private void Compare()
    {
        Comparison.Clear();
        HasComparison = false;
        if (CompareBefore == null || CompareAfter == null || ReferenceEquals(CompareBefore, CompareAfter)) return;
        var cmp = QuickBenchComparer.Compare(CompareBefore.Result, CompareAfter.Result);
        foreach (var m in cmp.Metrics)
        {
            var unit = m.Unit;
            Comparison.Add(new ComparisonRow
            {
                Name = Loc.T("metric." + m.Key),
                Before = m.Before is { } b ? b.ToString("N1") + " " + unit : "—",
                After = m.After is { } a ? a.ToString("N1") + " " + unit : "—",
                Absolute = m.AbsoluteChange is { } d ? (d >= 0 ? "+" : "") + d.ToString("N1") : "—",
                Percent = m.ImprovementPercent is { } p ? (p >= 0 ? "+" : "") + p.ToString("0.0") + " %" : "—",
                Verdict = Loc.T("verdict." + m.Verdict, m.NoiseFloorPercent.ToString("0.#")),
                VerdictKey = m.Verdict.ToString(),
                Confidence = m.Verdict == ChangeVerdict.NotComparable ? "" : Loc.T("confidence.level." + m.Confidence),
            });
        }
        IndexValue = cmp.IndexAfterRelativeToBefore ?? double.NaN;
        IndexText = cmp.IndexAfterRelativeToBefore is { } idx ? Loc.T("quick.index", idx.ToString("0.0")) : Loc.T("quick.indexNone");
        ComparisonTitle = Loc.T("quick.compareTitle", CompareBefore.Title, CompareAfter.Title)
            + (cmp.CpuTempDeltaC is { } dc ? " · " + Loc.T("quick.tempDelta", (dc >= 0 ? "+" : "") + dc.ToString("0.#")) : "");
        // Regression after an update: say when the driver or Windows changed between the two runs.
        var rb = CompareBefore.Result; var ra = CompareAfter.Result;
        if (rb.GpuDriver != null && ra.GpuDriver != null && rb.GpuDriver != ra.GpuDriver) ComparisonTitle += " · " + Loc.T("quick.driverChanged", rb.GpuDriver, ra.GpuDriver);
        if (rb.OsBuild != null && ra.OsBuild != null && rb.OsBuild != ra.OsBuild) ComparisonTitle += " · " + Loc.T("quick.osChanged");
        HasComparison = true;
    }

    [RelayCommand]
    private void DeleteRun(QuickRunRow? row)
    {
        if (row == null || !Dialogs.Confirm(Loc.T("quick.delete.title"), row.Title, Loc.T("common.delete"), true)) return;
        _shell.Services.QuickBenches.Delete(row.Result);
        History.Remove(row);
        if (ReferenceEquals(CompareBefore, row)) CompareBefore = null;
        if (ReferenceEquals(CompareAfter, row)) CompareAfter = null;
    }

    // ------------------------------------------------------------------ Benchmark lab
    [RelayCommand]
    private async Task Lab(string? test)
    {
        if (Running || test == null) return;
        if (_shell.Diagnostic == null) { Dialogs.Info(Loc.T("lab.title"), Loc.T("quick.needDiagnostic")); return; }
        if (test == "stress" && !Dialogs.Confirm(Loc.T("lab.stress.title"), Loc.T("lab.stress.confirm", StressMinutes), Loc.T("quick.start"), true)) return;
        if (test == "internet" && !NetworkConsent) { Dialogs.Info(Loc.T("lab.net.title"), Loc.T("lab.net.needConsent")); return; }
        Running = true;
        _cts = new CancellationTokenSource();
        LabResults.Clear();
        LabStatus = Loc.T("lab.running." + test);
        var ct = _cts.Token;
        try
        {
            var profile = _shell.Diagnostic.Profile;
            switch (test)
            {
                case "cpu": case "memory": case "storage": case "gpu": case "system":
                {
                    var comp = Enum.Parse<BenchComponent>(test, ignoreCase: true);
                    var opts = QuickBenchFactory.Options(_shell.Services, _shell.Monitoring, profile, "standard", new() { comp });
                    var r = await Task.Run(() => new QuickBenchRunner(opts).Run("lab-" + test, null, ct));
                    foreach (var m in r.Metrics) LabResults.Add(ToRow(m));
                    if (comp == BenchComponent.Cpu) LabStatus = Loc.T("throttle." + r.Throttling, r.ThrottlingDetail ?? "");
                    else LabStatus = Loc.T("lab.done");
                    if (comp == BenchComponent.Gpu && profile.PrimaryGpu?.DedicatedVramBytes is > 0 and var vram)
                        LabResults.Add(new MetricRow { Name = Loc.T("lab.vramCapacity"), Component = Loc.T("bench.component.Gpu"), Value = (vram / 1073741824.0).ToString("0.#"), Unit = "Go" });
                    break;
                }
                case "inference":
                {
                    var single = await Task.Run(() => InferenceProxy.Gflops(TimeSpan.FromSeconds(6), 1, ct));
                    var multi = await Task.Run(() => InferenceProxy.Gflops(TimeSpan.FromSeconds(8), Environment.ProcessorCount, ct));
                    LabResults.Add(new MetricRow { Name = Loc.T("lab.inference.single"), Component = Loc.T("bench.component.Cpu"), Value = single.ToString("N1"), Unit = "GFLOPS" });
                    LabResults.Add(new MetricRow { Name = Loc.T("lab.inference.multi"), Component = Loc.T("bench.component.Cpu"), Value = multi.ToString("N1"), Unit = "GFLOPS" });
                    LabStatus = Loc.T("lab.inference.note");
                    break;
                }
                case "lan": case "internet":
                {
                    var target = test == "lan" ? profile.Network.FirstOrDefault(n => n.IsUp && n.Gateway != null)?.Gateway : "1.1.1.1";
                    if (target == null) { LabStatus = Loc.T("lab.net.noGateway"); break; }
                    var p = await NetworkManager.LatencyTester.PingAsync(target, 50, 100, ct);
                    LabResults.Add(new MetricRow { Name = Loc.T("lab.net.avg"), Component = target, Value = Fmt(p.AvgMs, "0.0"), Unit = "ms" });
                    LabResults.Add(new MetricRow { Name = Loc.T("lab.net.jitter"), Component = target, Value = Fmt(p.JitterMs, "0.0"), Unit = "ms" });
                    LabResults.Add(new MetricRow { Name = Loc.T("lab.net.max"), Component = target, Value = Fmt(p.MaxMs, "0.0"), Unit = "ms" });
                    LabResults.Add(new MetricRow { Name = Loc.T("lab.net.loss"), Component = target, Value = p.LossPercent.ToString("0.#"), Unit = "%" });
                    LabStatus = Loc.T("lab.net.note");
                    break;
                }
                case "stress":
                    await RunCombinedStress(profile, ct);
                    break;
            }
        }
        catch (OperationCanceledException) { LabStatus = Loc.T("lab.cancelled"); }
        catch (Exception ex) { Core.Log.Error("Lab", test, ex); LabStatus = Loc.T("lab.failed", ex.Message); }
        finally { Running = false; }
    }

    /// <summary>CPU + GPU load together, watched every second; stops immediately above the safety thresholds.</summary>
    private async Task RunCombinedStress(Core.Hardware.MachineProfile profile, CancellationToken outer)
    {
        const double cpuLimit = 95, gpuLimit = 90;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var sensorsAvailable = _shell.Monitoring.Last?.CpuTemp != null || _shell.Monitoring.Last?.GpuTemp != null;
        var minutes = sensorsAvailable ? StressMinutes : Math.Min(StressMinutes, 2);
        var duration = TimeSpan.FromMinutes(minutes);
        var gpu = profile.PrimaryGpu;
        var cpuTask = Task.Run(() => CpuStressTest.Run(duration, Math.Max(1, Environment.ProcessorCount - 1), null, stop.Token));
        Task<GpuStabilityResult?> gpuTask = gpu is { IsSoftware: false }
            ? Task.Run<GpuStabilityResult?>(() => new D3D11StabilityTester(gpu.Luid, () => _shell.Monitoring.Last?.GpuTemp).Run(duration, stop.Token))
            : Task.FromResult<GpuStabilityResult?>(null);
        double maxCpu = double.NaN, maxGpu = double.NaN;
        string? abortReason = null;
        var start = DateTime.UtcNow;
        while (!cpuTask.IsCompleted || !gpuTask.IsCompleted)
        {
            await Task.Delay(1000);
            var s = _shell.Monitoring.Last;
            if (s?.CpuTemp is { } ct2) maxCpu = double.IsNaN(maxCpu) ? ct2 : Math.Max(maxCpu, ct2);
            if (s?.GpuTemp is { } gt) maxGpu = double.IsNaN(maxGpu) ? gt : Math.Max(maxGpu, gt);
            LabStatus = Loc.T("lab.stress.progress", (int)(DateTime.UtcNow - start).TotalSeconds, (int)duration.TotalSeconds, Fmt(s?.CpuTemp, "0"), Fmt(s?.GpuTemp, "0"));
            if (s?.CpuTemp >= cpuLimit) abortReason ??= Loc.T("lab.stress.abortCpu", cpuLimit);
            if (s?.GpuTemp >= gpuLimit) abortReason ??= Loc.T("lab.stress.abortGpu", gpuLimit);
            if (abortReason != null) stop.Cancel();
        }
        var cpu = await cpuTask;
        var g = await gpuTask;
        LabResults.Add(new MetricRow { Name = Loc.T("lab.stress.cpuErrors"), Component = Loc.T("bench.component.Cpu"), Value = cpu.Errors.ToString(), Unit = "" });
        if (g != null) LabResults.Add(new MetricRow { Name = Loc.T("lab.stress.gpuErrors"), Component = Loc.T("bench.component.Gpu"), Value = (g.ComputeErrors + g.MemoryErrors).ToString(), Unit = "" });
        LabResults.Add(new MetricRow { Name = Loc.T("lab.stress.maxCpu"), Component = Loc.T("bench.component.Cpu"), Value = double.IsNaN(maxCpu) ? "—" : maxCpu.ToString("0"), Unit = "°C" });
        LabResults.Add(new MetricRow { Name = Loc.T("lab.stress.maxGpu"), Component = Loc.T("bench.component.Gpu"), Value = double.IsNaN(maxGpu) ? "—" : maxGpu.ToString("0"), Unit = "°C" });
        var passed = cpu.Errors == 0 && (g == null || g.Passed) && abortReason == null && !outer.IsCancellationRequested;
        LabStatus = abortReason ?? (outer.IsCancellationRequested ? Loc.T("lab.cancelled") : Loc.T(passed ? "lab.stress.pass" : "lab.stress.fail"))
                  + (sensorsAvailable ? "" : " " + Loc.T("lab.stress.noSensors"));
    }
}
