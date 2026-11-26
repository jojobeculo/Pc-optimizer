namespace FrameCore.BenchmarkManager.QuickBench;

/// <summary>
/// Before/after comparison that refuses to present noise as progress:
/// a change counts only if it exceeds the metric's noise floor (at least twice the observed run-to-run variation)
/// AND the Welch t-test on the repeated samples is significant.
/// </summary>
public static class QuickBenchComparer
{
    public static QuickBenchComparison Compare(QuickBenchResult before, QuickBenchResult after)
    {
        var cmp = new QuickBenchComparison { Before = before, After = after };
        foreach (var b in before.Metrics)
        {
            var a = after.Get(b.Key);
            var m = new MetricComparison { Key = b.Key, Component = b.Component, Unit = b.Unit, NoiseFloorPercent = b.NoiseFloorPercent };
            cmp.Metrics.Add(m);
            if (a == null || !a.Measured || !b.Measured || b.Median == 0 || double.IsNaN(b.Median) || double.IsNaN(a.Median))
            {
                m.Verdict = ChangeVerdict.NotComparable;
                continue;
            }
            m.Before = b.Median;
            m.After = a.Median;
            var change = (a.Median - b.Median) / Math.Abs(b.Median) * 100;
            m.ImprovementPercent = b.HigherIsBetter ? change : -change;
            var noise = Math.Max(b.NoiseFloorPercent, 2 * Math.Max(b.CvPercent, a.CvPercent));
            m.NoiseFloorPercent = Math.Round(noise, 2);
            if (b.Samples.Count >= 2 && a.Samples.Count >= 2) m.PValue = SessionComparer.WelchTTest(b.Samples.ToArray(), a.Samples.ToArray());

            if (Math.Abs(m.ImprovementPercent.Value) < noise) { m.Verdict = ChangeVerdict.WithinNoise; m.Confidence = ConfidenceFor(m, b, a); continue; }
            if (m.PValue is not { } p) { m.Verdict = ChangeVerdict.Inconclusive; m.Confidence = ResultConfidence.Low; continue; }
            if (p < 0.05)
            {
                m.Verdict = m.ImprovementPercent > 0 ? ChangeVerdict.Improved : ChangeVerdict.Regressed;
                m.Confidence = p < 0.01 && Math.Min(a.Samples.Count, b.Samples.Count) >= 5 ? ResultConfidence.High : ResultConfidence.Medium;
            }
            else { m.Verdict = ChangeVerdict.Inconclusive; m.Confidence = ResultConfidence.Low; }
        }

        if (before.Thermals.CpuTempMaxC is { } c0 && after.Thermals.CpuTempMaxC is { } c1) cmp.CpuTempDeltaC = Math.Round(c1 - c0, 1);
        if (before.Thermals.GpuTempMaxC is { } g0 && after.Thermals.GpuTempMaxC is { } g1) cmp.GpuTempDeltaC = Math.Round(g1 - g0, 1);
        cmp.IndexAfterRelativeToBefore = Index(cmp);
        return cmp;
    }

    private static ResultConfidence ConfidenceFor(MetricComparison m, BenchMetric b, BenchMetric a) =>
        Math.Min(a.Samples.Count, b.Samples.Count) >= 5 && Math.Max(a.CvPercent, b.CvPercent) < 2 ? ResultConfidence.High
        : Math.Min(a.Samples.Count, b.Samples.Count) >= 3 ? ResultConfidence.Medium : ResultConfidence.Low;

    /// <summary>
    /// Relative index: per component, geometric mean of the metric ratios, where changes within noise count as 1.0;
    /// then geometric mean of the components. 100 = identical, 103 = 3 % better overall. Never an absolute score.
    /// </summary>
    public static double? Index(QuickBenchComparison cmp)
    {
        var comps = new List<double>();
        foreach (var g in cmp.Metrics.Where(m => m.Verdict != ChangeVerdict.NotComparable).GroupBy(m => m.Component))
        {
            var ratios = g.Select(m => m.Verdict is ChangeVerdict.Improved or ChangeVerdict.Regressed ? 1 + m.ImprovementPercent!.Value / 100 : 1.0)
                          .Where(r => r > 0).ToList();
            if (ratios.Count > 0) comps.Add(Math.Exp(ratios.Average(Math.Log)));
        }
        return comps.Count == 0 ? null : Math.Round(Math.Exp(comps.Average(Math.Log)) * 100, 1);
    }
}
