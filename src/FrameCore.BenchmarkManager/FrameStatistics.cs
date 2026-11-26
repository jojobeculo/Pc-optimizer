namespace FrameCore.BenchmarkManager;

public sealed class FrameStats
{
    public int Frames { get; set; }
    public double DurationSeconds { get; set; }
    /// <summary>Frames / total time (not the mean of per-frame FPS, which overweights fast frames).</summary>
    public double AverageFps { get; set; }
    public double AverageFrameTimeMs { get; set; }
    public double MedianFrameTimeMs { get; set; }
    public double P90FrameTimeMs { get; set; }
    public double P95FrameTimeMs { get; set; }
    public double P99FrameTimeMs { get; set; }
    public double? P999FrameTimeMs { get; set; }
    /// <summary>1 % low = average FPS of the slowest 1 % of frames.</summary>
    public double Low1PercentFps { get; set; }
    /// <summary>0.1 % low; null when fewer than 10 frames fall in the 0.1 % tail (unreliable).</summary>
    public double? Low01PercentFps { get; set; }
    /// <summary>Alternative definition: FPS equivalent of the 99th percentile frame time.</summary>
    public double P99Fps { get; set; }
    public double FrameTimeStdDevMs { get; set; }
    /// <summary>Frames longer than twice the median and at least 8 ms above it.</summary>
    public int Stutters { get; set; }
    public bool Reliable01 => Low01PercentFps != null;
}

/// <summary>Pure frame-time statistics, unit-tested with known sequences.</summary>
public static class FrameStatistics
{
    public const int MinFramesFor01Low = 10_000;

    public static FrameStats Compute(IReadOnlyList<double> frameTimesMs)
    {
        var valid = frameTimesMs.Where(f => f > 0 && f < 5000 && !double.IsNaN(f)).ToArray();
        if (valid.Length == 0) return new FrameStats();
        var sorted = (double[])valid.Clone();
        Array.Sort(sorted);
        var total = valid.Sum();
        var mean = total / valid.Length;
        var variance = valid.Sum(f => (f - mean) * (f - mean)) / valid.Length;
        var median = Percentile(sorted, 50);

        double TailAverageFps(double fraction)
        {
            var n = Math.Max(1, (int)Math.Ceiling(sorted.Length * fraction));
            var tail = sorted.Skip(sorted.Length - n).Average();
            return 1000.0 / tail;
        }

        var stats = new FrameStats
        {
            Frames = valid.Length,
            DurationSeconds = total / 1000.0,
            AverageFps = valid.Length / (total / 1000.0),
            AverageFrameTimeMs = mean,
            MedianFrameTimeMs = median,
            P90FrameTimeMs = Percentile(sorted, 90),
            P95FrameTimeMs = Percentile(sorted, 95),
            P99FrameTimeMs = Percentile(sorted, 99),
            FrameTimeStdDevMs = Math.Sqrt(variance),
            Low1PercentFps = TailAverageFps(0.01),
            Stutters = valid.Count(f => f > 2 * median && f > median + 8),
        };
        stats.P99Fps = 1000.0 / stats.P99FrameTimeMs;
        if (valid.Length >= MinFramesFor01Low)
        {
            stats.P999FrameTimeMs = Percentile(sorted, 99.9);
            stats.Low01PercentFps = TailAverageFps(0.001);
        }
        return stats;
    }

    /// <summary>Linear-interpolated percentile on a sorted array (same as Excel PERCENTILE.INC).</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        var rank = p / 100.0 * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    /// <summary>Frame times from successive present timestamps (ms).</summary>
    public static List<double> FromTimestamps(IReadOnlyList<double> timestampsMs)
    {
        var list = new List<double>(Math.Max(0, timestampsMs.Count - 1));
        for (var i = 1; i < timestampsMs.Count; i++) list.Add(timestampsMs[i] - timestampsMs[i - 1]);
        return list;
    }
}
