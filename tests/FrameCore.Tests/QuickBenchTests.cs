using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.BenchmarkManager.Verification;
using FrameCore.Core.Engine;
using FrameCore.Core.Models;
using Xunit;

namespace FrameCore.Tests;

public class QuickBenchTests
{
    private static QuickBenchResult Bench(params (string key, BenchComponent c, bool higher, double[] samples)[] metrics)
    {
        var r = new QuickBenchResult();
        foreach (var (key, c, higher, samples) in metrics)
            r.Metrics.Add(new BenchMetric { Key = key, Component = c, HigherIsBetter = higher, Samples = samples.ToList(), NoiseFloorPercent = 2 });
        return r;
    }

    [Fact]
    public void Statistics_are_robust()
    {
        var v = new double[] { 10, 11, 9, 10, 50 };
        Assert.Equal(10, BenchStatistics.Median(v));
        Assert.Equal(1, BenchStatistics.Mad(v));
        var (lo, hi) = BenchStatistics.Ci95(new double[] { 10, 10, 10 });
        Assert.Equal(10, lo); Assert.Equal(10, hi);
        Assert.Equal(99, BenchStatistics.Percentile(Enumerable.Range(0, 101).Select(i => (double)i).ToArray(), 99), 6);
    }

    [Fact]
    public void Noise_is_never_reported_as_a_gain()
    {
        var before = Bench(("cpu.multi", BenchComponent.Cpu, true, new double[] { 100, 104, 97, 102, 99, 101 }));
        var after = Bench(("cpu.multi", BenchComponent.Cpu, true, new double[] { 102, 99, 104, 100, 103, 101 }));
        var cmp = QuickBenchComparer.Compare(before, after);
        Assert.Equal(ChangeVerdict.WithinNoise, cmp.Metrics[0].Verdict);
        Assert.Equal(100, cmp.IndexAfterRelativeToBefore);
    }

    [Fact]
    public void A_clear_and_stable_improvement_is_reported_with_high_confidence()
    {
        var before = Bench(("mem.latency", BenchComponent.Memory, false, new double[] { 80, 80.4, 79.8, 80.1, 80.2, 79.9 }));
        var after = Bench(("mem.latency", BenchComponent.Memory, false, new double[] { 72, 72.3, 71.9, 72.1, 72.2, 71.8 }));
        var m = QuickBenchComparer.Compare(before, after).Metrics[0];
        Assert.Equal(ChangeVerdict.Improved, m.Verdict);
        Assert.Equal(ResultConfidence.High, m.Confidence);
        Assert.True(m.ImprovementPercent > 9); // lower latency = positive improvement
    }

    [Fact]
    public void A_single_run_can_never_be_called_significant()
    {
        var before = Bench(("gpu.compute", BenchComponent.Gpu, true, new double[] { 100 }));
        var after = Bench(("gpu.compute", BenchComponent.Gpu, true, new double[] { 110 }));
        Assert.Equal(ChangeVerdict.Inconclusive, QuickBenchComparer.Compare(before, after).Metrics[0].Verdict);
    }

    [Fact]
    public void Throttling_needs_sensor_evidence_to_be_detected()
    {
        Assert.Equal(ThrottleVerdict.Detected, ThrottleAnalysis.Analyse(85, new ThermalSummary { CpuTempMaxC = 96, PerformanceLimitMinPercent = 80, Samples = 10 }).Item1);
        Assert.Equal(ThrottleVerdict.None, ThrottleAnalysis.Analyse(98, new ThermalSummary { CpuTempMaxC = 96, Samples = 10 }).Item1);
        Assert.Equal(ThrottleVerdict.Unknown, ThrottleAnalysis.Analyse(85, new ThermalSummary()).Item1);
        Assert.Equal(ThrottleVerdict.PossibleThermal, ThrottleAnalysis.Analyse(85, new ThermalSummary { CpuTempMaxC = 92, Samples = 5 }).Item1);
    }

    [Fact]
    public void Runner_produces_repeated_samples_and_real_progress()
    {
        var progress = new List<double>();
        var runner = new QuickBenchRunner(new QuickBenchOptions
        {
            Mode = "express", Only = new() { BenchComponent.Cpu },
            CpuThroughput = (d, threads, _) => 1000.0 * threads,
        });
        var r = runner.Run("test", new SyncProgress<QuickBenchProgress>(p => progress.Add(p.OverallFraction)), CancellationToken.None);
        Assert.Equal(3, r.Get("cpu.single")!.Samples.Count);
        Assert.Equal(100, r.Get("cpu.sustainedRatio")!.Samples.Single());
        Assert.Equal(1.0, progress.Last(), 6);
        Assert.True(progress.Zip(progress.Skip(1)).All(p => p.Second >= p.First - 1e-9), "progress must never go backwards");
    }

    private sealed class SyncProgress<T>(Action<T> a) : IProgress<T> { public void Report(T v) => a(v); }
}

public class VerifyRepairTests
{
    /// <summary>Windows "forgets" the value: writes succeed but never stick.</summary>
    private sealed class ForgetfulExecutor : IActionExecutor
    {
        public int Writes;
        public int StickOnWrite = int.MaxValue;
        public string Type => "forget";
        public bool RequiresElevation(ActionDefinition a) => false;
        public ActionState Read(ActionDefinition a) => new() { Matches = Writes >= StickOnWrite, Current = "0", Target = "1" };
        public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of("0");
        public void Apply(ActionDefinition a) => Writes++;
        public void Restore(ActionDefinition a, StateSnapshot s) { }
        public bool Verify(ActionDefinition a, StateSnapshot s) => true;
    }

    private static (TweakEngine, ForgetfulExecutor, TweakDefinition) Setup(int stickOnWrite, string category = "Cpu")
    {
        var ex = new ForgetfulExecutor { StickOnWrite = stickOnWrite };
        var engine = new TweakEngine(new LocalDispatcher(new ExecutorRegistry(new IActionExecutor[] { ex })), new InMemoryJournal());
        var t = new TweakDefinition
        {
            Id = "t", Category = category, Name = new("t", "t"), Description = new("d", "d"), Confidence = Confidence.Confirmed,
            ImpactAreas = { ImpactArea.Fps }, Actions = { new ActionDefinition { Type = "forget" } },
        };
        return (engine, ex, t);
    }

    [Fact]
    public void Value_that_never_sticks_is_retried_twice_then_restored()
    {
        Repo.LoadLoc();
        var (engine, ex, t) = Setup(stickOnWrite: 99);
        var cs = engine.Apply(t, Sim.Facts(Sim.DesktopProfile())).ChangeSet!;
        var report = new VerifyRepairService(engine, _ => t).VerifyAndRepair(new[] { cs });
        Assert.Equal(VerifyStatus.RolledBackNotEffective, report.Items[0].Status);
        Assert.Equal(VerifyRepairService.MaxRepairAttempts, cs.RepairAttempts);
        Assert.Equal(1 + VerifyRepairService.MaxRepairAttempts, ex.Writes); // initial write + 2 retries, never more
        Assert.Equal(ChangeSetStatus.Restored, cs.Status);
    }

    [Fact]
    public void Value_that_sticks_on_second_write_is_repaired()
    {
        Repo.LoadLoc();
        var (engine, _, t) = Setup(stickOnWrite: 2);
        var cs = engine.Apply(t, Sim.Facts(Sim.DesktopProfile())).ChangeSet!;
        var report = new VerifyRepairService(engine, _ => t).VerifyAndRepair(new[] { cs });
        Assert.Equal(VerifyStatus.Repaired, report.Items[0].Status);
        Assert.Equal(1, cs.RepairAttempts);
    }

    private static QuickBenchResult Cpu(double median) => new()
    {
        Metrics = { new BenchMetric { Key = "cpu.multi", Component = BenchComponent.Cpu, Samples = Enumerable.Range(0, 6).Select(i => median + (i % 2 == 0 ? 0.2 : -0.2)).ToList() } },
    };

    [Fact]
    public void Confirmed_regression_is_undone_only_if_it_disappears()
    {
        Repo.LoadLoc();
        var (engine, _, t) = Setup(stickOnWrite: 1);
        var facts = Sim.Facts(Sim.DesktopProfile());
        var cs = engine.Apply(t, facts).ChangeSet!;
        var svc = new VerifyRepairService(engine, _ => t);
        var report = new VerifyRepairReport();
        svc.CheckPerformance(report, new[] { cs }, Cpu(100), Cpu(90), _ => Cpu(100), () => facts);
        Assert.Contains(report.Items, i => i.Status == VerifyStatus.RolledBackRegression);
        Assert.Equal(ChangeSetStatus.Restored, cs.Status);

        // Regression that persists without the change: the change is re-applied once.
        var (engine2, _, t2) = Setup(stickOnWrite: 1);
        var cs2 = engine2.Apply(t2, facts).ChangeSet!;
        var report2 = new VerifyRepairReport();
        new VerifyRepairService(engine2, _ => t2).CheckPerformance(report2, new[] { cs2 }, Cpu(100), Cpu(90), _ => Cpu(90), () => facts);
        Assert.Contains(report2.Items, i => i.Status == VerifyStatus.Reapplied);
    }

    [Fact]
    public void Network_and_privacy_changes_are_never_blamed_for_benchmark_results()
    {
        var t = new TweakDefinition { Category = "Network", ImpactAreas = { ImpactArea.NetworkLatency } };
        Assert.Empty(VerifyRepairService.ComponentsAffectedBy(t));
        t = new TweakDefinition { Category = "Privacy", ImpactAreas = { ImpactArea.Privacy } };
        Assert.Empty(VerifyRepairService.ComponentsAffectedBy(t));
    }
}

public class CombinedStressTests
{
    private sealed class FixedSensors(double? cpu, double? gpu) : IBenchSensors
    {
        public BenchSensorSample? Sample() => new(cpu, gpu, null, null, null, null, null, null);
    }

    [Fact]
    public void Thermal_guard_stops_the_load_at_once()
    {
        var o = new QuickBenchOptions
        {
            Mode = "complete",
            Sensors = new FixedSensors(96, 60), // CPU already above the 95 °C limit
            CombinedStress = (d, ct) => { ct.WaitHandle.WaitOne(d); return new StressOutcome(0, true, 0, false); },
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = QuickBenchRunner.RunCombinedStress(o, 30, null, CancellationToken.None);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal("cpuTemp", s.StopReason);
        Assert.Equal(96, s.CpuTempMaxC);
        Assert.False(s.Passed);
    }

    [Fact]
    public void Without_temperatures_the_stress_is_kept_short()
    {
        TimeSpan asked = default;
        var o = new QuickBenchOptions
        {
            Sensors = new FixedSensors(null, null),
            CombinedStress = (d, ct) => { asked = d; return new StressOutcome(0, false, 0, false); },
        };
        var s = QuickBenchRunner.RunCombinedStress(o, 180, null, CancellationToken.None);
        Assert.False(s.SensorsAvailable);
        Assert.Equal(TimeSpan.FromSeconds(QuickBenchRunner.UnsupervisedStressSeconds), asked);
        Assert.True(s.Passed);
    }

    [Fact]
    public void Calculation_errors_mean_unstable()
    {
        var o = new QuickBenchOptions
        {
            Sensors = new FixedSensors(70, 65),
            CombinedStress = (d, ct) => new StressOutcome(3, true, 1, false),
        };
        var s = QuickBenchRunner.RunCombinedStress(o, 5, null, CancellationToken.None);
        Assert.True(s.SensorsAvailable);
        Assert.Null(s.StopReason);
        Assert.Equal(3, s.CpuErrors);
        Assert.Equal(1, s.GpuErrors);
        Assert.False(s.Passed);
        var ok = QuickBenchRunner.RunCombinedStress(new QuickBenchOptions { Sensors = new FixedSensors(70, 65), CombinedStress = (d, ct) => new StressOutcome(0, true, 0, false) }, 5, null, CancellationToken.None);
        Assert.True(ok.Passed);
        Assert.Equal(70, ok.CpuTempMaxC);
    }
}
