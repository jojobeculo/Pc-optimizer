using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCore.Core;
using FrameCore.Core.Platform;

namespace FrameCore.MemoryManager;

public sealed class MemorySnapshot
{
    public DateTime Time { get; set; } = DateTime.Now;
    public long TotalBytes { get; set; }
    public long AvailableBytes { get; set; }
    public long UsedBytes => TotalBytes - AvailableBytes;
    public double UsedPercent => TotalBytes > 0 ? 100.0 * UsedBytes / TotalBytes : 0;
    public long CommitBytes { get; set; }
    public long CommitLimitBytes { get; set; }
    public long SystemCacheBytes { get; set; }
    public long StandbyBytes { get; set; }
    public long ModifiedBytes { get; set; }
    public long FreeZeroBytes { get; set; }
    public long KernelPagedBytes { get; set; }
    public long KernelNonPagedBytes { get; set; }
    public double? HardFaultsPerSec { get; set; }
    public double? CompressedBytes { get; set; }
}

/// <summary>
/// Read-only memory monitoring. FrameCore deliberately offers NO "RAM cleaner": standby memory is a cache Windows
/// gives back instantly; purging it only causes extra disk reads.
/// </summary>
public sealed class MemoryMonitor : IDisposable
{
    private readonly PdhQuery _pdh = new();

    public MemoryMonitor()
    {
        _pdh.Add("standbyNormal", @"\Memory\Standby Cache Normal Priority Bytes");
        _pdh.Add("standbyReserve", @"\Memory\Standby Cache Reserve Bytes");
        _pdh.Add("standbyCore", @"\Memory\Standby Cache Core Bytes");
        _pdh.Add("modified", @"\Memory\Modified Page List Bytes");
        _pdh.Add("free", @"\Memory\Free & Zero Page List Bytes");
        _pdh.Add("pageReads", @"\Memory\Page Reads/sec");
        _pdh.Add("compressed", @"\Process(MemCompression)\Working Set");
        _pdh.Collect();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)] private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, uint size);

    public MemorySnapshot Read()
    {
        var s = new MemorySnapshot();
        if (!OperatingSystem.IsWindows()) return s;
        if (GetPerformanceInfo(out var pi, (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>()))
        {
            var page = (long)pi.PageSize;
            s.TotalBytes = (long)pi.PhysicalTotal * page;
            s.AvailableBytes = (long)pi.PhysicalAvailable * page;
            s.CommitBytes = (long)pi.CommitTotal * page;
            s.CommitLimitBytes = (long)pi.CommitLimit * page;
            s.SystemCacheBytes = (long)pi.SystemCache * page;
            s.KernelPagedBytes = (long)pi.KernelPaged * page;
            s.KernelNonPagedBytes = (long)pi.KernelNonpaged * page;
        }
        _pdh.Collect();
        s.StandbyBytes = (long)((_pdh.Get("standbyNormal") ?? 0) + (_pdh.Get("standbyReserve") ?? 0) + (_pdh.Get("standbyCore") ?? 0));
        s.ModifiedBytes = (long)(_pdh.Get("modified") ?? 0);
        s.FreeZeroBytes = (long)(_pdh.Get("free") ?? 0);
        s.HardFaultsPerSec = _pdh.Get("pageReads");
        s.CompressedBytes = _pdh.Get("compressed");
        return s;
    }

    public void Dispose() => _pdh.Dispose();
}

public sealed class ProcessMemoryGroup
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public long PrivateBytes { get; set; }
    public long WorkingSet { get; set; }
    public double CpuPercent { get; set; }
    public bool HasWindow { get; set; }
}

public sealed class MemoryAnomaly
{
    public string Name { get; set; } = "";
    public long StartBytes { get; set; }
    public long EndBytes { get; set; }
    public TimeSpan Over { get; set; }
    public double GrowthMbPerMinute => Over.TotalMinutes > 0 ? (EndBytes - StartBytes) / 1048576.0 / Over.TotalMinutes : 0;
}

/// <summary>Tracks consumption per process name over time to show changes and detect suspicious growth (possible leaks).</summary>
public sealed class ProcessMemoryTracker
{
    private readonly ProcessSampler _sampler = new();
    private readonly Dictionary<string, List<(DateTime t, long bytes)>> _history = new(StringComparer.OrdinalIgnoreCase);
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    public List<ProcessMemoryGroup> Sample()
    {
        var now = DateTime.Now;
        var groups = _sampler.Sample().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessMemoryGroup
            {
                Name = g.Key, Count = g.Count(), PrivateBytes = g.Sum(x => x.PrivateBytes), WorkingSet = g.Sum(x => x.WorkingSet),
                CpuPercent = g.Sum(x => x.CpuPercent ?? 0), HasWindow = g.Any(x => x.HasWindow),
            })
            .OrderByDescending(g => g.PrivateBytes).ToList();
        foreach (var g in groups)
        {
            if (!_history.TryGetValue(g.Name, out var h)) _history[g.Name] = h = new();
            h.Add((now, g.PrivateBytes));
            h.RemoveAll(x => now - x.t > Window);
        }
        return groups;
    }

    /// <summary>Monotonic-ish growth of more than <paramref name="minGrowthMb"/> MB over at least 5 minutes.</summary>
    public List<MemoryAnomaly> Anomalies(double minGrowthMb = 400)
    {
        var list = new List<MemoryAnomaly>();
        foreach (var (name, h) in _history)
        {
            if (h.Count < 5) continue;
            var span = h[^1].t - h[0].t;
            if (span < TimeSpan.FromMinutes(5)) continue;
            var growth = (h[^1].bytes - h[0].bytes) / 1048576.0;
            var increasing = h.Zip(h.Skip(1), (a, b) => b.bytes >= a.bytes - 16 * 1048576).Count(x => x) >= (h.Count - 1) * 0.8;
            if (growth >= minGrowthMb && increasing)
                list.Add(new MemoryAnomaly { Name = name, StartBytes = h[0].bytes, EndBytes = h[^1].bytes, Over = span });
        }
        return list.OrderByDescending(a => a.GrowthMbPerMinute).ToList();
    }

    public IReadOnlyList<(DateTime t, long bytes)> History(string name) =>
        _history.TryGetValue(name, out var h) ? h : Array.Empty<(DateTime, long)>();
}

/// <summary>Windows Memory Diagnostic (mdsched.exe) and its results from the System event log.</summary>
public static class MemoryDiagnostic
{
    public static void Launch() => Process.Start(new ProcessStartInfo(SystemTools.PathOf(SystemTool.Mdsched)) { UseShellExecute = true });

    /// <summary>Last results: event 1101/1201 = no error, 1102/1202 = errors detected.</summary>
    public static List<EventRecordInfo> LastResults() =>
        EventLogQuery.Query("System", "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']]]", 5);
}
