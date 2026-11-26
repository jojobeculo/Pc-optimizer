using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.CpuManager;
using FrameCore.GpuManager;

namespace FrameCore.UI.Services;

/// <summary>Sensor readings for the quick benchmark, taken from the live monitoring service (no extra polling).</summary>
public sealed class MonitoringBenchSensors : IBenchSensors
{
    private readonly MonitoringService _m;
    public MonitoringBenchSensors(MonitoringService m) => _m = m;

    public BenchSensorSample? Sample()
    {
        var s = _m.Last;
        if (s == null) return null;
        var g = s.PrimaryGpu;
        return new BenchSensorSample(s.CpuTemp, s.GpuTemp ?? g?.TemperatureC, s.Cpu.EffectiveMhz, g?.CoreClockMhz,
            s.Cpu.TotalPercent, g?.UtilizationPercent, s.Cpu.PerformanceLimitPercent, s.Cpu.PerformanceLimitFlags);
    }
}

/// <summary>GPU workload of the quick benchmark: the Direct3D 11 compute + VRAM test of the GPU module.</summary>
public sealed class D3D11GpuBench : IGpuBench
{
    private readonly long? _luid;
    private readonly Func<double?> _temp;

    public D3D11GpuBench(long? luid, bool hasHardwareGpu, Func<double?> temperature)
    {
        _luid = luid;
        _temp = temperature;
        UnavailableReason = hasHardwareGpu ? null : "noGpu";
    }

    public string? UnavailableReason { get; }

    public (double compute, double vramGBs) Run(TimeSpan duration, CancellationToken ct)
    {
        var r = new D3D11StabilityTester(_luid, _temp) { VramMegabytes = 256 }.Run(duration, ct);
        if (r.Error != null) throw new InvalidOperationException(r.Error);
        return (r.ComputeScore, r.BandwidthGBs);
    }
}

public static class QuickBenchFactory
{
    public static QuickBenchOptions Options(AppServices s, MonitoringService m, Core.Hardware.MachineProfile? profile, string mode, HashSet<BenchComponent>? only = null)
    {
        var gpu = profile?.PrimaryGpu;
        return new QuickBenchOptions
        {
            Mode = mode,
            Only = only,
            CpuThroughput = (d, threads, ct) => CpuStressTest.Run(d, threads, null, ct).Score,
            Sensors = new MonitoringBenchSensors(m),
            Gpu = new D3D11GpuBench(gpu?.Luid, gpu is { IsSoftware: false }, () => m.Last?.GpuTemp),
            StorageFolder = Core.AppPaths.Root,
        };
    }
}
