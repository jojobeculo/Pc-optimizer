using System.Diagnostics;
using System.Numerics;

namespace FrameCore.BenchmarkManager.QuickBench;

/// <summary>
/// CPU matrix-multiplication throughput (FP32 GFLOPS), the core operation of neural-network inference.
/// It is a proxy: it shows how fast this CPU and its memory feed dense math, not the speed of a specific AI model.
/// </summary>
public static class InferenceProxy
{
    public static double Gflops(TimeSpan duration, int threads, CancellationToken ct)
    {
        const int n = 256; // 256×256 matrices: 33.5 MFLOP per multiplication, fits in L2/L3
        threads = Math.Clamp(threads, 1, Environment.ProcessorCount);
        long ops = 0;
        var sw = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct }, _ =>
        {
            var a = new float[n * n]; var b = new float[n * n]; var c = new float[n * n];
            var rng = new Random(7);
            for (var i = 0; i < a.Length; i++) { a[i] = (float)rng.NextDouble(); b[i] = (float)rng.NextDouble(); }
            var bt = new float[n * n];
            for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) bt[j * n + i] = b[i * n + j];
            var w = Vector<float>.Count;
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                for (var i = 0; i < n; i++)
                {
                    var row = a.AsSpan(i * n, n);
                    for (var j = 0; j < n; j++)
                    {
                        var col = bt.AsSpan(j * n, n);
                        var acc = Vector<float>.Zero;
                        var k = 0;
                        for (; k <= n - w; k += w) acc += new Vector<float>(row[k..]) * new Vector<float>(col[k..]);
                        var sum = Vector.Dot(acc, Vector<float>.One);
                        for (; k < n; k++) sum += row[k] * col[k];
                        c[i * n + j] = sum;
                    }
                }
                Interlocked.Increment(ref ops);
            }
            GC.KeepAlive(c);
        });
        return ops * 2.0 * n * n * n / sw.Elapsed.TotalSeconds / 1e9;
    }
}
