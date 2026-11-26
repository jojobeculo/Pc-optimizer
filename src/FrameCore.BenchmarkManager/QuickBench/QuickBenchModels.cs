using System.Text.Json.Serialization;

namespace FrameCore.BenchmarkManager.QuickBench;

public enum BenchComponent { Cpu, Memory, Gpu, Storage, System }

/// <summary>One measured quantity with all its repeated samples (nothing is reduced to a single run).</summary>
public sealed class BenchMetric
{
    public string Key { get; set; } = "";
    public BenchComponent Component { get; set; }
    public string Unit { get; set; } = "";
    public bool HigherIsBetter { get; set; } = true;
    public List<double> Samples { get; set; } = new();
    /// <summary>Relative change below which a difference is treated as measurement noise for this metric (percent).</summary>
    public double NoiseFloorPercent { get; set; } = 3;
    /// <summary>Why the metric could not be measured (null when measured).</summary>
    public string? UnavailableReason { get; set; }

    [JsonIgnore] public bool Measured => Samples.Count > 0 && UnavailableReason == null;
    [JsonIgnore] public double Median => BenchStatistics.Median(Samples);
    [JsonIgnore] public double CvPercent => BenchStatistics.CvPercent(Samples);
    [JsonIgnore] public (double low, double high) Ci95 => BenchStatistics.Ci95(Samples);
}

/// <summary>Sensor observations collected during the run (null = sensor not available on this machine).</summary>
public sealed class ThermalSummary
{
    public double? CpuTempMaxC { get; set; }
    public double? CpuTempAvgC { get; set; }
    public double? GpuTempMaxC { get; set; }
    public double? CpuClockAvgMhz { get; set; }
    public double? CpuClockMinUnderLoadMhz { get; set; }
    public double? GpuClockAvgMhz { get; set; }
    public double? CpuUsageAvgPercent { get; set; }
    public double? GpuUsageMaxPercent { get; set; }
    /// <summary>Lowest "% Performance Limit" seen under load (100 = no limitation).</summary>
    public double? PerformanceLimitMinPercent { get; set; }
    /// <summary>Union of "Performance Limit Flags" seen under load (thermal, power, current…).</summary>
    public uint? PerformanceLimitFlags { get; set; }
    public int Samples { get; set; }
}

public enum ThrottleVerdict { None, PossibleThermal, PossiblePower, Detected, Unknown }

public sealed class QuickBenchResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public string Label { get; set; } = "";
    public string Mode { get; set; } = "standard";
    public string MachineSignature { get; set; } = "";
    /// <summary>Optimisations active when the benchmark ran (change set ids), to relate results to changes.</summary>
    public List<string> ActiveTweakIds { get; set; } = new();
    public List<BenchMetric> Metrics { get; set; } = new();
    public ThermalSummary Thermals { get; set; } = new();
    public ThrottleVerdict Throttling { get; set; } = ThrottleVerdict.Unknown;
    public string? ThrottlingDetail { get; set; }
    public bool Cancelled { get; set; }
    public List<string> Notes { get; set; } = new();

    public BenchMetric? Get(string key) => Metrics.FirstOrDefault(m => m.Key == key);
}

public enum ChangeVerdict { Improved, Regressed, WithinNoise, Inconclusive, NotComparable }

public enum ResultConfidence { Low, Medium, High }

/// <summary>Before/after comparison of one metric.</summary>
public sealed class MetricComparison
{
    public string Key { get; set; } = "";
    public BenchComponent Component { get; set; }
    public string Unit { get; set; } = "";
    public double? Before { get; set; }
    public double? After { get; set; }
    public double? AbsoluteChange => Before is { } b && After is { } a ? a - b : null;
    /// <summary>Signed change in percent, positive = better (the sign takes "lower is better" into account).</summary>
    public double? ImprovementPercent { get; set; }
    public double? PValue { get; set; }
    public double NoiseFloorPercent { get; set; }
    public ChangeVerdict Verdict { get; set; }
    public ResultConfidence Confidence { get; set; }
}

public sealed class QuickBenchComparison
{
    public QuickBenchResult Before { get; set; } = null!;
    public QuickBenchResult After { get; set; } = null!;
    public List<MetricComparison> Metrics { get; set; } = new();
    public double? CpuTempDeltaC { get; set; }
    public double? GpuTempDeltaC { get; set; }
    /// <summary>Geometric mean of per-component ratios (after/before) × 100; null when nothing comparable.</summary>
    public double? IndexAfterRelativeToBefore { get; set; }
    public IEnumerable<MetricComparison> Regressions => Metrics.Where(m => m.Verdict == ChangeVerdict.Regressed);
}
