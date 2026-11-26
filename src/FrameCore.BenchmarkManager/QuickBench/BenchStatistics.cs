namespace FrameCore.BenchmarkManager.QuickBench;

/// <summary>Robust statistics for repeated benchmark samples (median, MAD, coefficient of variation, 95 % CI of the mean).</summary>
public static class BenchStatistics
{
    public static double Median(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }

    public static double Mean(IReadOnlyList<double> v) => v.Count == 0 ? double.NaN : v.Average();

    public static double StdDev(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        var m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
    }

    /// <summary>Median absolute deviation (robust spread).</summary>
    public static double Mad(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return double.NaN;
        var med = Median(v);
        return Median(v.Select(x => Math.Abs(x - med)).ToArray());
    }

    /// <summary>Coefficient of variation in percent (0 when fewer than two samples).</summary>
    public static double CvPercent(IReadOnlyList<double> v)
    {
        var m = Mean(v);
        return v.Count < 2 || m == 0 ? 0 : StdDev(v) / Math.Abs(m) * 100;
    }

    /// <summary>Two-sided 95 % Student t quantile for <paramref name="df"/> degrees of freedom.</summary>
    public static double T95(int df) => df switch
    {
        <= 0 => double.NaN, 1 => 12.706, 2 => 4.303, 3 => 3.182, 4 => 2.776, 5 => 2.571, 6 => 2.447, 7 => 2.365, 8 => 2.306,
        9 => 2.262, 10 => 2.228, 11 => 2.201, 12 => 2.179, 13 => 2.160, 14 => 2.145, 15 => 2.131, < 20 => 2.10, < 30 => 2.05, _ => 1.96,
    };

    /// <summary>95 % confidence interval of the mean.</summary>
    public static (double low, double high) Ci95(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return (double.NaN, double.NaN);
        var m = Mean(v);
        var h = T95(v.Count - 1) * StdDev(v) / Math.Sqrt(v.Count);
        return (m - h, m + h);
    }

    /// <summary>Percentile with linear interpolation (p in 0..100).</summary>
    public static double Percentile(IReadOnlyList<double> v, double p)
    {
        if (v.Count == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToArray();
        var rank = p / 100 * (s.Length - 1);
        var lo = (int)Math.Floor(rank); var hi = (int)Math.Ceiling(rank);
        return s[lo] + (s[hi] - s[lo]) * (rank - lo);
    }
}
