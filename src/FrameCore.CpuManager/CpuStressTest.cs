using System.Diagnostics;

namespace FrameCore.CpuManager;

public sealed class StressProgress
{
    public TimeSpan Elapsed { get; set; }
    public long Iterations { get; set; }
    public long Errors { get; set; }
    public double IterationsPerSecond { get; set; }
}

public sealed class StressResult
{
    public TimeSpan Duration { get; set; }
    public int Threads { get; set; }
    public long Iterations { get; set; }
    public long Errors { get; set; }
    public double Score { get; set; }
    public bool Cancelled { get; set; }
    public bool Passed => Errors == 0 && !Cancelled;
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Deterministic CPU load with result verification. Every work unit must reproduce a reference checksum computed
/// before the test; any mismatch is a computation error — a strong sign of instability (overclock, undervolt,
/// overheating, faulty RAM). A short pass is never a guarantee of long-term stability.
/// </summary>
public static class CpuStressTest
{
    public const int UnitSize = 16_384;

    /// <summary>One deterministic work unit: integer hashing + IEEE-754 FMA chains (results are bit-exact).</summary>
    public static ulong WorkUnit(uint seed)
    {
        ulong acc = 0xcbf29ce484222325UL;
        uint x = seed * 2654435761u + 1;
        double f = 1.0 + (seed & 0xFF) / 256.0;
        for (var i = 0; i < UnitSize; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            acc = (acc ^ x) * 0x100000001b3UL;
            f = Math.FusedMultiplyAdd(f, 1.0000001, (x & 0xFFFF) * 1e-9);
            if (f > 1e6) f -= 1e6;
            if ((i & 1023) == 0) acc ^= (ulong)BitConverter.DoubleToInt64Bits(f);
        }
        return acc ^ (ulong)BitConverter.DoubleToInt64Bits(f);
    }

    public static StressResult Run(TimeSpan duration, int threads, IProgress<StressProgress>? progress, CancellationToken ct)
    {
        threads = Math.Clamp(threads, 1, Environment.ProcessorCount);
        const int seeds = 64;
        var reference = new ulong[seeds];
        for (uint s = 0; s < seeds; s++) reference[s] = WorkUnit(s);

        long iterations = 0, errors = 0;
        var sw = Stopwatch.StartNew();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = new Thread[threads];
        for (var t = 0; t < threads; t++)
        {
            var offset = (uint)t;
            workers[t] = new Thread(() =>
            {
                uint s = offset % seeds;
                while (!stop.IsCancellationRequested)
                {
                    var r = WorkUnit(s);
                    if (r != reference[s]) Interlocked.Increment(ref errors);
                    Interlocked.Increment(ref iterations);
                    s = (s + 1) % seeds;
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "FrameCore stress " + t };
            workers[t].Start();
        }

        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            Thread.Sleep(500);
            progress?.Report(new StressProgress
            {
                Elapsed = sw.Elapsed, Iterations = Interlocked.Read(ref iterations), Errors = Interlocked.Read(ref errors),
                IterationsPerSecond = Interlocked.Read(ref iterations) / Math.Max(0.001, sw.Elapsed.TotalSeconds),
            });
        }
        stop.Cancel();
        foreach (var w in workers) w.Join(5000);
        sw.Stop();
        return new StressResult
        {
            Duration = sw.Elapsed, Threads = threads, Iterations = iterations, Errors = errors, Cancelled = ct.IsCancellationRequested,
            Score = iterations / Math.Max(0.001, sw.Elapsed.TotalSeconds),
        };
    }

    /// <summary>Short single + multi-thread benchmark used for before/after comparisons (score = work units / s).</summary>
    public static (double single, double multi) Benchmark(TimeSpan each, CancellationToken ct = default)
    {
        var single = Run(each, 1, null, ct).Score;
        var multi = Run(each, Environment.ProcessorCount, null, ct).Score;
        return (single, multi);
    }
}
