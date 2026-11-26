using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCore.Core;

namespace FrameCore.BenchmarkManager.QuickBench;

/// <summary>One sensor reading taken during the benchmark (null fields = not available).</summary>
public sealed record BenchSensorSample(
    double? CpuTempC, double? GpuTempC, double? CpuClockMhz, double? GpuClockMhz,
    double? CpuUsagePercent, double? GpuUsagePercent, double? PerformanceLimitPercent, uint? PerformanceLimitFlags);

public interface IBenchSensors { BenchSensorSample? Sample(); }

/// <summary>GPU workload (implemented with Direct3D 11 compute in the GPU module).</summary>
public interface IGpuBench
{
    string? UnavailableReason { get; }
    /// <summary>Runs for <paramref name="duration"/>; returns compute throughput (passes/s) and VRAM bandwidth (GB/s).</summary>
    (double compute, double vramGBs) Run(TimeSpan duration, CancellationToken ct);
}

public sealed record QuickBenchProgress(int Step, int StepCount, string StepKey, double StepFraction, double OverallFraction);

public sealed class QuickBenchOptions
{
    /// <summary>"standard" ≈ 4–5 min, "express" ≈ 1.5 min (used for automatic verification).</summary>
    public string Mode { get; set; } = "standard";
    /// <summary>CPU work-unit throughput for a duration and thread count (units/s).</summary>
    public Func<TimeSpan, int, CancellationToken, double> CpuThroughput { get; set; } = (_, _, _) => double.NaN;
    public IGpuBench? Gpu { get; set; }
    public IBenchSensors? Sensors { get; set; }
    /// <summary>Folder for the temporary storage test file (deleted afterwards).</summary>
    public string? StorageFolder { get; set; }
    public long StorageFileBytes { get; set; } = 512L * 1024 * 1024;
    /// <summary>Components to run (all by default) — the benchmark lab runs them one at a time.</summary>
    public HashSet<BenchComponent>? Only { get; set; }
}

/// <summary>
/// The quick benchmark. Every test is time-boxed so progress is real (elapsed / planned), every metric keeps all
/// its repeated samples, and the run is interrupted cleanly on cancellation.
/// </summary>
public sealed class QuickBenchRunner
{
    private readonly QuickBenchOptions _o;
    private readonly List<BenchSensorSample> _loadSamples = new();
    public QuickBenchRunner(QuickBenchOptions options) => _o = options;

    private bool Express => _o.Mode == "express";
    private bool Runs(BenchComponent c) => _o.Only == null || _o.Only.Contains(c);

    private sealed record Step(string Key, BenchComponent Component, double PlannedSeconds, Action<QuickBenchResult, Action<double>, CancellationToken> Body);

    public QuickBenchResult Run(string label, IProgress<QuickBenchProgress>? progress, CancellationToken ct)
    {
        var result = new QuickBenchResult { Label = label, Mode = _o.Mode };
        var sw = Stopwatch.StartNew();
        var reps = Express ? 3 : 6;
        var cpuSeconds = Express ? 2.0 : 4.0;
        var steps = new List<Step>
        {
            new("cpu.single", BenchComponent.Cpu, reps * cpuSeconds, (r, p, c) => Cpu(r, "cpu.single", 1, reps, cpuSeconds, p, c)),
            new("cpu.multi", BenchComponent.Cpu, reps * cpuSeconds, (r, p, c) => Cpu(r, "cpu.multi", Environment.ProcessorCount, reps, cpuSeconds, p, c)),
            new("cpu.sustained", BenchComponent.Cpu, Express ? 20 : 60, (r, p, c) => Sustained(r, Express ? 20 : 60, p, c)),
            new("mem.bandwidth", BenchComponent.Memory, Express ? 3 : 6, (r, p, c) => MemoryBandwidth(r, Express ? 5 : 9, p, c)),
            new("mem.latency", BenchComponent.Memory, Express ? 6 : 12, (r, p, c) => MemoryLatency(r, Express ? 2 : 4, p, c)),
            new("disk", BenchComponent.Storage, Express ? 15 : 35, (r, p, c) => Storage(r, Express ? 1 : 2, Express ? 2 : 4, p, c)),
            new("gpu", BenchComponent.Gpu, Express ? 24 : 45, (r, p, c) => Gpu(r, Express ? 2 : 3, Express ? 10 : 15, p, c)),
            new("sys.wake", BenchComponent.System, Express ? 6 : 12, (r, p, c) => Responsiveness(r, Express ? 3 : 4, Express ? 2 : 3, p, c)),
        };
        steps = steps.Where(s => Runs(s.Component)).ToList();
        var total = steps.Sum(s => s.PlannedSeconds);
        double done = 0;
        for (var i = 0; i < steps.Count && !ct.IsCancellationRequested; i++)
        {
            var s = steps[i];
            var stepIndex = i;
            void Report(double f) => progress?.Report(new QuickBenchProgress(stepIndex + 1, steps.Count, s.Key, Math.Clamp(f, 0, 1), Math.Clamp((done + s.PlannedSeconds * Math.Clamp(f, 0, 1)) / total, 0, 1)));
            Report(0);
            try { s.Body(result, Report, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error("QuickBench", $"step {s.Key} failed", ex);
                result.Notes.Add($"{s.Key}: {ex.Message}");
            }
            Report(1);
            done += s.PlannedSeconds;
        }
        result.Cancelled = ct.IsCancellationRequested;
        result.Duration = sw.Elapsed;
        Summarise(result);
        return result;
    }

    // ------------------------------------------------------------------ CPU
    private void Cpu(QuickBenchResult r, string key, int threads, int reps, double seconds, Action<double> p, CancellationToken ct)
    {
        var m = new BenchMetric { Key = key, Component = BenchComponent.Cpu, Unit = "u/s", NoiseFloorPercent = 2 };
        r.Metrics.Add(m);
        _o.CpuThroughput(TimeSpan.FromSeconds(0.5), threads, ct); // warm-up: clocks ramp up, code is jitted
        for (var i = 0; i < reps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var v = _o.CpuThroughput(TimeSpan.FromSeconds(seconds), threads, ct);
            if (!double.IsNaN(v)) m.Samples.Add(Math.Round(v, 1));
            p((i + 1.0) / reps);
        }
    }

    /// <summary>Long all-core load cut in windows: a falling throughput reveals thermal or power throttling.</summary>
    private void Sustained(QuickBenchResult r, double seconds, Action<double> p, CancellationToken ct)
    {
        var m = new BenchMetric { Key = "cpu.sustained", Component = BenchComponent.Cpu, Unit = "u/s", NoiseFloorPercent = 3 };
        var ratio = new BenchMetric { Key = "cpu.sustainedRatio", Component = BenchComponent.Cpu, Unit = "%", NoiseFloorPercent = 3 };
        r.Metrics.Add(m); r.Metrics.Add(ratio);
        const double window = 5;
        var windows = (int)Math.Max(2, seconds / window);
        for (var i = 0; i < windows; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sensorTask = SampleWhile(TimeSpan.FromSeconds(window), ct);
            var v = _o.CpuThroughput(TimeSpan.FromSeconds(window), Environment.ProcessorCount, ct);
            sensorTask.Wait(ct);
            if (!double.IsNaN(v)) m.Samples.Add(Math.Round(v, 1));
            p((i + 1.0) / windows);
        }
        if (m.Samples.Count >= 2)
        {
            var first = m.Samples.Take(2).Average();
            var last = m.Samples.Skip(m.Samples.Count - 2).Average();
            ratio.Samples.Add(Math.Round(last / first * 100, 1));
        }
    }

    private Task SampleWhile(TimeSpan duration, CancellationToken ct) => Task.Run(() =>
    {
        if (_o.Sensors == null) return;
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            try { if (_o.Sensors.Sample() is { } s) lock (_loadSamples) _loadSamples.Add(s); } catch { /* sensors are optional */ }
            Thread.Sleep(1000);
        }
    }, ct);

    // ------------------------------------------------------------------ Memory
    private static void MemoryBandwidth(QuickBenchResult r, int copies, Action<double> p, CancellationToken ct)
    {
        var m = new BenchMetric { Key = "mem.bandwidth", Component = BenchComponent.Memory, Unit = "GB/s", NoiseFloorPercent = 3 };
        r.Metrics.Add(m);
        const int size = 256 * 1024 * 1024;
        var src = GC.AllocateUninitializedArray<byte>(size);
        var dst = GC.AllocateUninitializedArray<byte>(size);
        src.AsSpan().Fill(0x5A);
        src.AsSpan().CopyTo(dst); // first touch: page faults are not part of the measurement
        for (var i = 0; i < copies; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            src.AsSpan().CopyTo(dst);
            sw.Stop();
            m.Samples.Add(Math.Round(2.0 * size / sw.Elapsed.TotalSeconds / 1e9, 2));
            p((i + 1.0) / copies);
        }
    }

    private static void MemoryLatency(QuickBenchResult r, int runs, Action<double> p, CancellationToken ct)
    {
        var m = new BenchMetric { Key = "mem.latency", Component = BenchComponent.Memory, Unit = "ns", HigherIsBetter = false, NoiseFloorPercent = 3 };
        r.Metrics.Add(m);
        const int count = 32 * 1024 * 1024; // 256 MB: far beyond any cache
        var next = new int[count];
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        var rng = new Random(42);
        for (var i = count - 1; i > 0; i--) { var j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        for (var i = 0; i < count - 1; i++) next[order[i]] = order[i + 1];
        next[order[^1]] = order[0];
        const int steps = 10_000_000;
        for (var run = 0; run < runs; run++)
        {
            ct.ThrowIfCancellationRequested();
            var idx = order[run * 7919 % count];
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < steps; i++) idx = next[idx];
            sw.Stop();
            GC.KeepAlive(idx);
            m.Samples.Add(Math.Round(sw.Elapsed.TotalMilliseconds * 1e6 / steps, 1));
            p((run + 1.0) / runs);
        }
    }

    // ------------------------------------------------------------------ Storage
    private void Storage(QuickBenchResult r, int passes, int randomRuns, Action<double> p, CancellationToken ct)
    {
        var write = new BenchMetric { Key = "disk.seqWrite", Component = BenchComponent.Storage, Unit = "MB/s", NoiseFloorPercent = 8 };
        var read = new BenchMetric { Key = "disk.seqRead", Component = BenchComponent.Storage, Unit = "MB/s", NoiseFloorPercent = 8 };
        var iops = new BenchMetric { Key = "disk.rand4kRead", Component = BenchComponent.Storage, Unit = "IOPS", NoiseFloorPercent = 10 };
        var lat = new BenchMetric { Key = "disk.rand4kP99", Component = BenchComponent.Storage, Unit = "µs", HigherIsBetter = false, NoiseFloorPercent = 15 };
        r.Metrics.AddRange(new[] { write, read, iops, lat });
        var folder = _o.StorageFolder ?? Path.GetTempPath();
        var path = Path.Combine(folder, $"framecore-bench-{Guid.NewGuid():N}.tmp");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            if (drive.AvailableFreeSpace < _o.StorageFileBytes * 4)
            {
                foreach (var m in new[] { write, read, iops, lat }) m.UnavailableReason = "lowDiskSpace";
                return;
            }
            var total = passes * 2 + randomRuns;
            var n = 0;
            for (var i = 0; i < passes; i++)
            {
                write.Samples.Add(Math.Round(StorageIo.SequentialWrite(path, _o.StorageFileBytes, ct), 0)); p(++n / (double)total);
                read.Samples.Add(Math.Round(StorageIo.SequentialRead(path, ct), 0)); p(++n / (double)total);
            }
            var latencies = new List<double>();
            for (var i = 0; i < randomRuns; i++)
            {
                var (rate, lats) = StorageIo.RandomRead4K(path, TimeSpan.FromSeconds(Express ? 2 : 3), ct);
                iops.Samples.Add(Math.Round(rate, 0));
                lat.Samples.Add(Math.Round(BenchStatistics.Percentile(lats, 99), 1));
                latencies.AddRange(lats);
                p(++n / (double)total);
            }
        }
        finally { try { File.Delete(path); } catch { /* temp file, best effort */ } }
    }

    // ------------------------------------------------------------------ GPU
    private void Gpu(QuickBenchResult r, int runs, double seconds, Action<double> p, CancellationToken ct)
    {
        var compute = new BenchMetric { Key = "gpu.compute", Component = BenchComponent.Gpu, Unit = "passes/s", NoiseFloorPercent = 3 };
        var vram = new BenchMetric { Key = "gpu.vram", Component = BenchComponent.Gpu, Unit = "GB/s", NoiseFloorPercent = 4 };
        r.Metrics.Add(compute); r.Metrics.Add(vram);
        if (_o.Gpu == null || _o.Gpu.UnavailableReason != null)
        {
            compute.UnavailableReason = vram.UnavailableReason = _o.Gpu?.UnavailableReason ?? "noGpu";
            return;
        }
        for (var i = 0; i < runs; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sensors = SampleWhile(TimeSpan.FromSeconds(seconds), ct);
            var (c, v) = _o.Gpu.Run(TimeSpan.FromSeconds(seconds), ct);
            try { sensors.Wait(ct); } catch (OperationCanceledException) { throw; } catch { /* ignore */ }
            if (c > 0) compute.Samples.Add(Math.Round(c, 1));
            if (v > 0) vram.Samples.Add(Math.Round(v, 1));
            p((i + 1.0) / runs);
        }
    }

    // ------------------------------------------------------------------ System responsiveness
    /// <summary>
    /// Asks the OS to wake a thread every millisecond (high-resolution timer) and measures how late it wakes up.
    /// Driver interrupts (DPC/ISR), timer coalescing and background load show up as lateness.
    /// </summary>
    private static void Responsiveness(QuickBenchResult r, int runs, double seconds, Action<double> p, CancellationToken ct)
    {
        var p50 = new BenchMetric { Key = "sys.wakeP50", Component = BenchComponent.System, Unit = "µs", HigherIsBetter = false, NoiseFloorPercent = 15 };
        var p99 = new BenchMetric { Key = "sys.wakeP99", Component = BenchComponent.System, Unit = "µs", HigherIsBetter = false, NoiseFloorPercent = 20 };
        r.Metrics.Add(p50); r.Metrics.Add(p99);
        for (var i = 0; i < runs; i++)
        {
            ct.ThrowIfCancellationRequested();
            var late = WakeLatency.Measure(TimeSpan.FromSeconds(seconds), ct);
            if (late.Count > 0)
            {
                p50.Samples.Add(Math.Round(BenchStatistics.Percentile(late, 50), 1));
                p99.Samples.Add(Math.Round(BenchStatistics.Percentile(late, 99), 1));
            }
            p((i + 1.0) / runs);
        }
    }

    // ------------------------------------------------------------------ Sensors & throttling
    private void Summarise(QuickBenchResult r)
    {
        List<BenchSensorSample> s;
        lock (_loadSamples) s = _loadSamples.ToList();
        var t = r.Thermals;
        t.Samples = s.Count;
        static double? Max(IEnumerable<double?> v) { var l = v.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : Math.Round(l.Max(), 1); }
        static double? Avg(IEnumerable<double?> v) { var l = v.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : Math.Round(l.Average(), 1); }
        static double? Min(IEnumerable<double?> v) { var l = v.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : Math.Round(l.Min(), 1); }
        t.CpuTempMaxC = Max(s.Select(x => x.CpuTempC));
        t.CpuTempAvgC = Avg(s.Select(x => x.CpuTempC));
        t.GpuTempMaxC = Max(s.Select(x => x.GpuTempC));
        t.CpuClockAvgMhz = Avg(s.Select(x => x.CpuClockMhz));
        t.CpuClockMinUnderLoadMhz = Min(s.Select(x => x.CpuClockMhz));
        t.GpuClockAvgMhz = Avg(s.Select(x => x.GpuClockMhz));
        t.CpuUsageAvgPercent = Avg(s.Select(x => x.CpuUsagePercent));
        t.GpuUsageMaxPercent = Max(s.Select(x => x.GpuUsagePercent));
        t.PerformanceLimitMinPercent = Min(s.Select(x => x.PerformanceLimitPercent));
        var flags = s.Where(x => x.PerformanceLimitFlags is > 0).Select(x => x.PerformanceLimitFlags!.Value).ToList();
        t.PerformanceLimitFlags = flags.Count == 0 ? (s.Any(x => x.PerformanceLimitFlags.HasValue) ? 0u : null) : flags.Aggregate(0u, (a, b) => a | b);
        (r.Throttling, r.ThrottlingDetail) = ThrottleAnalysis.Analyse(r.Get("cpu.sustainedRatio")?.Samples.FirstOrDefault(), t);
    }
}

public static class ThrottleAnalysis
{
    /// <summary>
    /// Throttling is only "detected" when the sustained throughput drops AND the processor reports a limitation
    /// or a high temperature. A drop without sensor evidence stays "possible".
    /// </summary>
    public static (ThrottleVerdict, string?) Analyse(double? sustainedRatioPercent, ThermalSummary t)
    {
        if (sustainedRatioPercent is not { } ratio) return (ThrottleVerdict.Unknown, null);
        var drop = 100 - ratio;
        var hot = t.CpuTempMaxC is >= 90;
        var limited = t.PerformanceLimitMinPercent is < 95 || t.PerformanceLimitFlags is > 0;
        if (drop < 7) return (ThrottleVerdict.None, $"{drop:0.#}");
        if (hot && limited) return (ThrottleVerdict.Detected, $"{drop:0.#}");
        if (hot) return (ThrottleVerdict.PossibleThermal, $"{drop:0.#}");
        if (limited) return (ThrottleVerdict.PossiblePower, $"{drop:0.#}");
        return (t.Samples == 0 || t.CpuTempMaxC == null ? ThrottleVerdict.Unknown : ThrottleVerdict.PossiblePower, $"{drop:0.#}");
    }
}

/// <summary>Unbuffered file I/O (FILE_FLAG_NO_BUFFERING on Windows) so the OS cache does not fake the results.</summary>
internal static unsafe class StorageIo
{
    private const int Block = 1024 * 1024;
    private const int Small = 4096;
    private static FileOptions Unbuffered => OperatingSystem.IsWindows() ? (FileOptions)0x20000000 | FileOptions.WriteThrough : FileOptions.None;

    public static double SequentialWrite(string path, long bytes, CancellationToken ct)
    {
        var buf = (byte*)NativeMemory.AlignedAlloc(Block, Small);
        try
        {
            var span = new Span<byte>(buf, Block);
            for (var i = 0; i < Block; i++) span[i] = (byte)(i * 31 + 7); // incompressible enough for controllers that compress
            using var h = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, Unbuffered, bytes);
            var sw = Stopwatch.StartNew();
            for (long off = 0; off < bytes; off += Block)
            {
                ct.ThrowIfCancellationRequested();
                RandomAccess.Write(h, span, off);
            }
            RandomAccess.FlushToDisk(h);
            return bytes / 1048576.0 / sw.Elapsed.TotalSeconds;
        }
        finally { NativeMemory.AlignedFree(buf); }
    }

    public static double SequentialRead(string path, CancellationToken ct)
    {
        var buf = (byte*)NativeMemory.AlignedAlloc(Block, Small);
        try
        {
            var span = new Span<byte>(buf, Block);
            using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, Unbuffered);
            var len = RandomAccess.GetLength(h);
            var sw = Stopwatch.StartNew();
            for (long off = 0; off < len; off += Block)
            {
                ct.ThrowIfCancellationRequested();
                RandomAccess.Read(h, span, off);
            }
            return len / 1048576.0 / sw.Elapsed.TotalSeconds;
        }
        finally { NativeMemory.AlignedFree(buf); }
    }

    /// <summary>Queue depth 1 random 4 KiB reads: the latency a game sees when it streams many small assets.</summary>
    public static (double iops, List<double> latenciesUs) RandomRead4K(string path, TimeSpan duration, CancellationToken ct)
    {
        var buf = (byte*)NativeMemory.AlignedAlloc(Small, Small);
        try
        {
            var span = new Span<byte>(buf, Small);
            using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, Unbuffered | FileOptions.RandomAccess);
            var blocks = RandomAccess.GetLength(h) / Small;
            var rng = new Random(1234);
            var lat = new List<double>(100_000);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                var t0 = Stopwatch.GetTimestamp();
                RandomAccess.Read(h, span, rng.NextInt64(blocks) * Small);
                lat.Add(Stopwatch.GetElapsedTime(t0).TotalMicroseconds);
            }
            return (lat.Count / sw.Elapsed.TotalSeconds, lat);
        }
        finally { NativeMemory.AlignedFree(buf); }
    }
}

/// <summary>Timer wake-up lateness, in microseconds.</summary>
internal static class WakeLatency
{
    public static List<double> Measure(TimeSpan duration, CancellationToken ct)
    {
        var late = new List<double>(4096);
        var period = TimeSpan.FromMilliseconds(1);
        var sw = Stopwatch.StartNew();
        IntPtr timer = IntPtr.Zero;
        if (OperatingSystem.IsWindows())
            timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        try
        {
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                var t0 = Stopwatch.GetTimestamp();
                if (timer != IntPtr.Zero)
                {
                    long due = -10_000; // 1 ms, relative, in 100 ns units
                    SetWaitableTimerEx(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
                    WaitForSingleObject(timer, 1000);
                }
                else Thread.Sleep(period);
                var elapsedUs = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
                late.Add(Math.Max(0, elapsedUs - 1000));
            }
        }
        finally { if (timer != IntPtr.Zero) CloseHandle(timer); }
        return late;
    }

    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x1F0003;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetWaitableTimerEx(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, IntPtr wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
