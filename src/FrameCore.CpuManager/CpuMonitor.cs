using System.Globalization;
using FrameCore.Core.Platform;

namespace FrameCore.CpuManager;

public sealed class CpuSample
{
    public DateTime Time { get; set; } = DateTime.Now;
    public double? TotalPercent { get; set; }
    public List<double> PerCore { get; set; } = new();
    public double? EffectiveMhz { get; set; }
    public double? NominalMhz { get; set; }
    /// <summary>"% Performance Limit": ceiling allowed by firmware/thermal/power limits (100 = no limitation).</summary>
    public double? PerformanceLimitPercent { get; set; }
    /// <summary>"Performance Limit Flags": non-zero when the processor reports an active limitation.</summary>
    public uint? PerformanceLimitFlags { get; set; }
    public double MaxCorePercent => PerCore.Count > 0 ? PerCore.Max() : TotalPercent ?? 0;
    public float? TemperatureC { get; set; }
    public float? PackagePowerW { get; set; }
}

/// <summary>Driver-less CPU monitoring through PDH "Processor Information" counters (same source as Task Manager).</summary>
public sealed class CpuMonitor : IDisposable
{
    private readonly PdhQuery _pdh = new();

    public CpuMonitor()
    {
        if (!_pdh.Add("total", @"\Processor Information(_Total)\% Processor Utility"))
            _pdh.Add("total", @"\Processor Information(_Total)\% Processor Time");
        if (!_pdh.Add("cores", @"\Processor Information(*)\% Processor Utility"))
            _pdh.Add("cores", @"\Processor Information(*)\% Processor Time");
        _pdh.Add("perf", @"\Processor Information(_Total)\% Processor Performance");
        _pdh.Add("freq", @"\Processor Information(_Total)\Processor Frequency");
        _pdh.Add("limit", @"\Processor Information(_Total)\% Performance Limit");
        _pdh.Add("flags", @"\Processor Information(_Total)\Performance Limit Flags");
        _pdh.Collect();
    }

    public bool Available => _pdh.IsValid;

    public CpuSample Read()
    {
        _pdh.Collect();
        var s = new CpuSample
        {
            TotalPercent = Clamp(_pdh.Get("total")),
            NominalMhz = _pdh.Get("freq"),
            PerformanceLimitPercent = _pdh.Get("limit"),
            PerformanceLimitFlags = _pdh.Get("flags") is { } fl ? (uint)Math.Max(0, fl) : null,
        };
        var perf = _pdh.Get("perf");
        if (perf != null && s.NominalMhz != null) s.EffectiveMhz = s.NominalMhz * perf / 100.0;
        s.PerCore = _pdh.GetArray("cores")
            .Where(kv => !kv.Key.Contains("_Total", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => SortKey(kv.Key))
            .Select(kv => Math.Clamp(kv.Value, 0, 100)).ToList();
        return s;
    }

    private static double? Clamp(double? v) => v == null ? null : Math.Clamp(v.Value, 0, 100);

    /// <summary>Instances are "group,index" (e.g. "0,12").</summary>
    private static int SortKey(string instance)
    {
        var parts = instance.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out var g) && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? g * 1000 + i : int.MaxValue;
    }

    public void Dispose() => _pdh.Dispose();
}
