using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager;

public sealed class BottleneckInput
{
    public string Target { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string? Resolution { get; set; }
    public double? RefreshHz { get; set; }
    public FrameStats? Frames { get; set; }
    public List<SensorPoint> Samples { get; set; } = new();
    public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
    public double? RamTotalGb { get; set; }
    public double? VramTotalGb { get; set; }
    public List<string> GpuLimitReasons { get; set; } = new();
    public List<(string name, double cpu)> BackgroundProcesses { get; set; } = new();
}

/// <summary>
/// Interprets real measurements taken during a capture. There is no "bottleneck %": the result depends on the game,
/// scene, resolution and settings, so every finding states its context and its confidence.
/// </summary>
public static class BottleneckAnalyzer
{
    public static List<Finding> Analyze(BottleneckInput i)
    {
        var f = new List<Finding>();
        var context = Loc.T("bottleneck.context", i.Target, i.DurationSeconds.ToString("0"), i.Resolution ?? Loc.Unknown);
        if (i.Samples.Count < 5)
        {
            f.Add(new Finding { Id = "bottleneck.insufficient", Category = "Benchmark", Severity = Severity.Info, Title = Loc.T("bottleneck.insufficient"), Context = context });
            return f;
        }
        double? Avg(Func<SensorPoint, double?> s) { var v = i.Samples.Select(s).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Average() : null; }
        double? Max(Func<SensorPoint, double?> s) { var v = i.Samples.Select(s).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Max() : null; }

        var gpu = Avg(s => s.Gpu);
        var cpu = Avg(s => s.Cpu);
        var maxCore = Avg(s => s.CpuMaxCore);
        var perfLimit = Avg(s => s.PerfLimit);
        var vram = Max(s => s.VramUsedGb);
        var ram = Max(s => s.RamUsedGb);
        var gpuTemp = Max(s => s.GpuTemp);
        var cpuTemp = Max(s => s.CpuTemp);

        // FPS capped (V-Sync / limiter): very stable frame times close to the refresh period.
        var capped = false;
        if (i.Frames is { Frames: > 100 } fr && i.RefreshHz is { } hz && hz > 0)
        {
            var nearRefresh = Math.Abs(fr.AverageFps - hz) / hz < 0.03 || Math.Abs(fr.AverageFps - hz / 2) / hz < 0.02;
            capped = nearRefresh && fr.FrameTimeStdDevMs < 1.0;
            if (capped)
                f.Add(Make("bottleneck.capped", Severity.Info, Confidence.Probable, context, fr.AverageFps.ToString("0"), hz.ToString("0")));
        }

        if (!capped && gpu is { } g && g >= 95)
            f.Add(Make("bottleneck.gpu", Severity.Info, Confidence.Probable, context, g.ToString("0")));
        else if (!capped && gpu is { } g2 && g2 < 85 && maxCore is { } mc && mc >= 85)
            f.Add(Make("bottleneck.cpu", Severity.Suggestion, Confidence.Probable, context, g2.ToString("0"), mc.ToString("0"), (cpu ?? 0).ToString("0")));
        else if (!capped && gpu is { } g3 && g3 < 80 && maxCore is { } mc2 && mc2 < 70)
            f.Add(Make("bottleneck.neither", Severity.Info, Confidence.Experimental, context, g3.ToString("0"), mc2.ToString("0")));

        if (vram is { } v && TotalVram(i) is { } vt && vt > 0 && v >= vt * 0.95)
            f.Add(Make("bottleneck.vram", Severity.Warning, Confidence.Probable, context, v.ToString("0.0"), vt.ToString("0.0")));
        if (ram is { } r && i.RamTotalGb is { } rt && r >= rt * 0.90)
            f.Add(Make("bottleneck.ram", Severity.Warning, Confidence.Probable, context, r.ToString("0.0"), rt.ToString("0.0")));
        if (perfLimit is { } pl && pl >= 5)
            f.Add(Make("bottleneck.cpuThrottle", Severity.Warning, Confidence.Confirmed, context, pl.ToString("0")));
        if (cpuTemp is { } ct && ct >= 95)
            f.Add(Make("bottleneck.cpuHot", Severity.Warning, Confidence.Probable, context, ct.ToString("0")));
        if (gpuTemp is { } gt && gt >= 85)
            f.Add(Make("bottleneck.gpuHot", Severity.Warning, Confidence.Probable, context, gt.ToString("0")));
        if (i.GpuLimitReasons.Contains("thermal"))
            f.Add(Make("bottleneck.gpuThermalLimit", Severity.Warning, Confidence.Confirmed, context));
        if (i.GpuLimitReasons.Contains("power") && gpu is >= 90)
            f.Add(Make("bottleneck.gpuPowerLimit", Severity.Info, Confidence.Confirmed, context));
        if (i.Frames is { Stutters: > 0 } st && st.Frames > 0 && st.Stutters * 1000.0 / st.Frames > 5)
            f.Add(Make("bottleneck.stutter", Severity.Suggestion, Confidence.Probable, context, st.Stutters, (st.Stutters * 100.0 / st.Frames).ToString("0.0")));
        foreach (var (name, pcpu) in i.BackgroundProcesses.Where(b => b.cpu >= 100.0 / i.LogicalProcessors * 0.5 && b.cpu >= 3).Take(3))
            f.Add(Make("bottleneck.background", Severity.Suggestion, Confidence.Probable, context, name, pcpu.ToString("0.0")));
        return f;
    }

    private static double? TotalVram(BottleneckInput i) => i.VramTotalGb;

    private static Finding Make(string key, Severity sev, Confidence conf, string context, params object[] args) => new()
    {
        Id = key, Category = "Benchmark", Severity = sev, Confidence = conf, Context = context,
        Title = Loc.T(key + ".title"), Detail = Loc.T(key + ".detail", args), Recommendation = Loc.T(key + ".reco"),
    };
}

/// <summary>Synthetic, repeatable micro-benchmarks for before/after comparisons when no game is used.</summary>
public static class SyntheticBenchmarks
{
    /// <summary>Memory copy bandwidth in GB/s (512 MB buffers, best of 5).</summary>
    public static double MemoryBandwidthGBs()
    {
        const int size = 256 * 1024 * 1024;
        var src = GC.AllocateUninitializedArray<byte>(size);
        var dst = GC.AllocateUninitializedArray<byte>(size);
        new Random(1).NextBytes(src.AsSpan(0, 4096));
        src.AsSpan().Fill(0x5A);
        double best = 0;
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            src.AsSpan().CopyTo(dst);
            sw.Stop();
            best = Math.Max(best, 2.0 * size / sw.Elapsed.TotalSeconds / 1e9); // read + write
        }
        return best;
    }

    /// <summary>Average memory latency (ns) using a random pointer chase over 256 MB (defeats caches and prefetchers).</summary>
    public static double MemoryLatencyNs()
    {
        const int count = 32 * 1024 * 1024; // 32M × 8 bytes = 256 MB
        var next = new long[count];
        var order = Enumerable.Range(0, count).ToArray();
        var rng = new Random(42);
        for (var i = count - 1; i > 0; i--) { var j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        for (var i = 0; i < count - 1; i++) next[order[i]] = order[i + 1];
        next[order[^1]] = order[0];
        const int steps = 20_000_000;
        long p = order[0];
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < steps; i++) p = next[p];
        sw.Stop();
        GC.KeepAlive(p);
        return sw.Elapsed.TotalMilliseconds * 1e6 / steps;
    }
}
