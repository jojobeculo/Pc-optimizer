using FrameCore.Core;
using FrameCore.Core.Localization;

namespace FrameCore.BenchmarkManager;

public sealed class SensorSummary
{
    public double? CpuAvgPercent { get; set; }
    public double? CpuMaxCorePercent { get; set; }
    public double? CpuEffectiveMhz { get; set; }
    public double? CpuTempMaxC { get; set; }
    public double? CpuPerfLimitPercent { get; set; }
    public double? GpuAvgPercent { get; set; }
    public double? GpuTempMaxC { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuPowerW { get; set; }
    public double? VramUsedGb { get; set; }
    public double? VramTotalGb { get; set; }
    public double? RamUsedGb { get; set; }
    public double? RamTotalGb { get; set; }
    public List<string> GpuLimitReasons { get; set; } = new();
}

public sealed class BenchmarkRun
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public FrameStats Stats { get; set; } = new();
    public SensorSummary Sensors { get; set; } = new();
    public List<double>? FrameTimesMs { get; set; }
    public List<SensorPoint> Timeline { get; set; } = new();
}

public sealed class SensorPoint
{
    public double T { get; set; }
    public double? Cpu { get; set; }
    public double? CpuMaxCore { get; set; }
    public double? Gpu { get; set; }
    public double? CpuTemp { get; set; }
    public double? GpuTemp { get; set; }
    public double? CpuMhz { get; set; }
    public double? GpuMhz { get; set; }
    public double? RamUsedGb { get; set; }
    public double? VramUsedGb { get; set; }
    public double? PerfLimit { get; set; }
    public double? Fps { get; set; }
}

public sealed class BenchmarkConditions
{
    public string? Resolution { get; set; }
    public double? RefreshHz { get; set; }
    public string? PowerPlan { get; set; }
    public string? GpuDriver { get; set; }
    public string? AppliedProfile { get; set; }
    public string? GpuTuning { get; set; }
    public string? Notes { get; set; }
}

public enum CaptureMethod { Etw, PresentMon, CsvImport, Synthetic }

public sealed class BenchmarkSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    /// <summary>before | after | driver | overclock | custom</summary>
    public string Kind { get; set; } = "custom";
    public string Target { get; set; } = "";
    public CaptureMethod Method { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public BenchmarkConditions Conditions { get; set; } = new();
    public List<BenchmarkRun> Runs { get; set; } = new();
    /// <summary>Synthetic scores (cpu.single, cpu.multi, mem.bandwidth, mem.latency) when Method = Synthetic.</summary>
    public Dictionary<string, double> Scores { get; set; } = new();

    public double Mean(Func<FrameStats, double> metric) => Runs.Count == 0 ? 0 : Runs.Average(r => metric(r.Stats));

    public double StdDev(Func<FrameStats, double> metric)
    {
        if (Runs.Count < 2) return 0;
        var m = Mean(metric);
        return Math.Sqrt(Runs.Sum(r => Math.Pow(metric(r.Stats) - m, 2)) / (Runs.Count - 1));
    }
}

public sealed class BenchmarkStore
{
    private readonly string _dir;
    public BenchmarkStore(string? dir = null) => _dir = dir ?? AppPaths.Benchmarks;

    public void Save(BenchmarkSession s) => Json.WriteAtomic(Path.Combine(_dir, $"{s.CreatedUtc:yyyyMMddHHmmss}-{s.Id}.json"), s);

    public List<BenchmarkSession> LoadAll()
    {
        var list = new List<BenchmarkSession>();
        foreach (var f in Directory.GetFiles(_dir, "*.json"))
            try { if (Json.Read<BenchmarkSession>(f) is { } s) list.Add(s); } catch (Exception ex) { Log.Warn("Bench", ex.Message); }
        return list.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public void Delete(BenchmarkSession s)
    {
        foreach (var f in Directory.GetFiles(_dir, $"*-{s.Id}.json")) File.Delete(f);
    }
}

public sealed class MetricComparison
{
    public string Metric { get; set; } = "";
    public double A { get; set; }
    public double B { get; set; }
    public double DeltaPercent => A == 0 ? 0 : (B - A) / A * 100.0;
    public bool HigherIsBetter { get; set; } = true;
    /// <summary>significant | withinNoise | insufficientRuns</summary>
    public string Verdict { get; set; } = "";
    public double? PValue { get; set; }
    public string Explanation { get; set; } = "";
}

/// <summary>
/// Before/after comparison. A difference is called significant only with ≥ 3 runs per session and a Welch t-test
/// p &lt; 0.05; otherwise it is compared to a noise floor (run-to-run variation, minimum 2 %). No gain is claimed without measurements.
/// </summary>
public static class SessionComparer
{
    public const double MinNoisePercent = 2.0;

    public static List<MetricComparison> Compare(BenchmarkSession a, BenchmarkSession b)
    {
        var list = new List<MetricComparison>
        {
            Make(Loc.T("bench.metric.avgFps"), a, b, s => s.AverageFps, true),
            Make(Loc.T("bench.metric.low1"), a, b, s => s.Low1PercentFps, true),
            Make(Loc.T("bench.metric.p99"), a, b, s => s.P99FrameTimeMs, false),
            Make(Loc.T("bench.metric.stddev"), a, b, s => s.FrameTimeStdDevMs, false),
        };
        if (a.Runs.All(r => r.Stats.Low01PercentFps != null) && b.Runs.All(r => r.Stats.Low01PercentFps != null) && a.Runs.Count > 0 && b.Runs.Count > 0)
            list.Insert(2, Make(Loc.T("bench.metric.low01"), a, b, s => s.Low01PercentFps ?? 0, true));
        return list;
    }

    private static MetricComparison Make(string name, BenchmarkSession a, BenchmarkSession b, Func<FrameStats, double> f, bool higherIsBetter)
    {
        var c = new MetricComparison { Metric = name, A = a.Mean(f), B = b.Mean(f), HigherIsBetter = higherIsBetter };
        var xa = a.Runs.Select(r => f(r.Stats)).ToArray();
        var xb = b.Runs.Select(r => f(r.Stats)).ToArray();
        if (xa.Length >= 3 && xb.Length >= 3)
        {
            c.PValue = WelchTTest(xa, xb);
            c.Verdict = c.PValue < 0.05 ? "significant" : "withinNoise";
            c.Explanation = Loc.T(c.Verdict == "significant" ? "bench.cmp.significant" : "bench.cmp.notSignificant", c.PValue?.ToString("0.000"));
        }
        else
        {
            var noise = Math.Max(MinNoisePercent, c.A == 0 ? 0 : Math.Max(a.StdDev(f), b.StdDev(f)) / c.A * 100.0 * 2);
            c.Verdict = Math.Abs(c.DeltaPercent) > noise ? "insufficientRuns" : "withinNoise";
            c.Explanation = Loc.T(c.Verdict == "withinNoise" ? "bench.cmp.withinNoise" : "bench.cmp.fewRuns", noise.ToString("0.0"));
        }
        return c;
    }

    /// <summary>Two-sided Welch's t-test p-value (Student t CDF via regularized incomplete beta).</summary>
    public static double WelchTTest(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average();
        double va = a.Sum(x => (x - ma) * (x - ma)) / (a.Length - 1);
        double vb = b.Sum(x => (x - mb) * (x - mb)) / (b.Length - 1);
        var se2 = va / a.Length + vb / b.Length;
        if (se2 <= 0) return ma == mb ? 1 : 0;
        var t = (ma - mb) / Math.Sqrt(se2);
        var df = se2 * se2 / (Math.Pow(va / a.Length, 2) / (a.Length - 1) + Math.Pow(vb / b.Length, 2) / (b.Length - 1));
        var x = df / (df + t * t);
        return RegularizedIncompleteBeta(df / 2, 0.5, x);
    }

    private static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var lbeta = LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x);
        var front = Math.Exp(lbeta);
        if (x < (a + 1) / (a + b + 2)) return front * ContinuedFraction(a, b, x) / a;
        return 1 - front * ContinuedFraction(b, a, 1 - x) / b;
    }

    private static double ContinuedFraction(double a, double b, double x)
    {
        const double eps = 1e-14, fpmin = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1, c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < fpmin) d = fpmin;
        d = 1 / d;
        var h = d;
        for (var m = 1; m <= 300; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
            c = 1 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
            c = 1 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1 / d;
            var del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }

    private static double LogGamma(double x)
    {
        double[] cof = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        var ser = 1.000000000190015;
        foreach (var c in cof) ser += c / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }
}
