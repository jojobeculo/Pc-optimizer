using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.CpuManager;
using FrameCore.GpuManager;
using FrameCore.HardwareDetection;
using FrameCore.MemoryManager;

namespace FrameCore.UI.Services;

public sealed class MonitorSnapshot
{
    public DateTime Time { get; set; } = DateTime.Now;
    public CpuSample Cpu { get; set; } = new();
    public List<GpuSample> Gpus { get; set; } = new();
    public MemorySnapshot Memory { get; set; } = new();
    public SensorSnapshot? Sensors { get; set; }
    public GpuSample? PrimaryGpu => Gpus.OrderByDescending(g => g.DedicatedTotalBytes ?? 0).FirstOrDefault();
    public double? CpuTemp { get; set; }
    public double? CpuPowerW { get; set; }
    public double? GpuTemp { get; set; }
    public double? GpuHotspot { get; set; }
}

/// <summary>
/// One-second sampling loop (PDH + NVML + LibreHardwareMonitor sensors). Advanced CPU sensors come from the elevated
/// worker when the user enabled them and PawnIO is installed; otherwise those values stay "Indéterminé".
/// </summary>
public sealed class MonitoringService : IDisposable
{
    private readonly PrivilegedClient _privileged;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private CpuMonitor? _cpu;
    private GpuMonitor? _gpu;
    private MemoryMonitor? _mem;
    private SensorService? _localSensors;
    private int _tick;

    public IReadOnlyList<GpuInfo> Gpus { get; set; } = Array.Empty<GpuInfo>();
    public MonitorSnapshot? Last { get; private set; }
    public RingBuffer CpuHistory { get; } = new(120);
    public RingBuffer GpuHistory { get; } = new(120);
    public RingBuffer RamHistory { get; } = new(120);
    public RingBuffer CpuTempHistory { get; } = new(120);
    public RingBuffer GpuTempHistory { get; } = new(120);
    public event EventHandler<MonitorSnapshot>? Updated;
    public bool Running => _cts != null;

    public MonitoringService(PrivilegedClient privileged, AppSettings settings) { _privileged = privileged; _settings = settings; }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            try
            {
                _cpu = new CpuMonitor();
                _gpu = new GpuMonitor();
                _mem = new MemoryMonitor();
            }
            catch (Exception ex) { Log.Error("Monitor", "init", ex); }
            while (!token.IsCancellationRequested)
            {
                try { Sample(); } catch (Exception ex) { Log.Warn("Monitor", ex.Message); }
                try { await Task.Delay(Math.Max(500, _settings.MonitoringIntervalMs), token); } catch { break; }
            }
        }, token);
    }

    private void Sample()
    {
        var s = new MonitorSnapshot
        {
            Cpu = _cpu?.Read() ?? new CpuSample(),
            Gpus = _gpu?.Read(Gpus) ?? new List<GpuSample>(),
            Memory = _mem?.Read() ?? new MemorySnapshot(),
        };
        if (_tick++ % 2 == 0)
        {
            try
            {
                s.Sensors = _settings.AdvancedSensors && _privileged.IsConnected
                    ? _privileged.ReadSensors()
                    : (_localSensors ??= new SensorService()).Read();
            }
            catch (Exception ex) { Log.Warn("Monitor", "sensors: " + ex.Message); }
        }
        else s.Sensors = Last?.Sensors;
        if (s.Sensors != null)
        {
            s.CpuTemp = Plausible(s.Sensors.Find("Cpu", "Temperature", "CPU Package", "Core (Tctl/Tdie)", "Core Average", "CPU Cores") ?? s.Sensors.FindContains("Cpu", "Temperature", "Tctl"));
            s.CpuPowerW = s.Sensors.Find("Cpu", "Power", "CPU Package", "Package");
            s.GpuTemp = Plausible(s.Sensors.Find("GpuNvidia", "Temperature", "GPU Core") ?? s.Sensors.Find("GpuAmd", "Temperature", "GPU Core") ?? s.Sensors.Find("GpuIntel", "Temperature", "GPU Core"));
            s.GpuHotspot = Plausible(s.Sensors.Find("GpuNvidia", "Temperature", "GPU Hot Spot") ?? s.Sensors.Find("GpuAmd", "Temperature", "GPU Hot Spot"));
            s.Cpu.TemperatureC = (float?)s.CpuTemp;
            s.Cpu.PackagePowerW = (float?)s.CpuPowerW;
            if (s.PrimaryGpu is { } pg)
            {
                pg.TemperatureC ??= s.GpuTemp;
                pg.HotspotC ??= s.GpuHotspot;
                pg.PowerW ??= s.Sensors.Find("GpuAmd", "Power", "GPU Package", "GPU Core") ?? s.Sensors.Find("GpuIntel", "Power", "GPU Package");
                pg.CoreClockMhz ??= s.Sensors.Find("GpuAmd", "Clock", "GPU Core") ?? s.Sensors.Find("GpuIntel", "Clock", "GPU Core");
            }
        }
        s.GpuTemp ??= s.PrimaryGpu?.TemperatureC;
        CpuHistory.Add(s.Cpu.TotalPercent ?? double.NaN);
        GpuHistory.Add(s.PrimaryGpu?.UtilizationPercent ?? double.NaN);
        RamHistory.Add(s.Memory.UsedPercent);
        if (s.CpuTemp != null) CpuTempHistory.Add(s.CpuTemp.Value);
        if (s.GpuTemp != null) GpuTempHistory.Add(s.GpuTemp.Value);
        Last = s;
        Updated?.Invoke(this, s);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        _cpu?.Dispose(); _gpu?.Dispose(); _mem?.Dispose(); _localSensors?.Dispose();
    }
    /// <summary>A die temperature outside 5–125 °C is a sensor artefact (VM, missing driver), never a real reading.</summary>
    public static double? Plausible(double? celsius) => celsius is > 5 and < 125 ? celsius : null;
}

public sealed class RingBuffer
{
    private readonly double[] _data;
    private int _count, _start;
    private readonly object _gate = new();
    public RingBuffer(int capacity) => _data = new double[capacity];
    public int Capacity => _data.Length;

    public void Add(double v)
    {
        lock (_gate)
        {
            if (_count < _data.Length) _data[(_start + _count++) % _data.Length] = v;
            else { _data[_start] = v; _start = (_start + 1) % _data.Length; }
        }
    }

    public double[] Snapshot()
    {
        lock (_gate)
        {
            var r = new double[_count];
            for (var i = 0; i < _count; i++) r[i] = _data[(_start + i) % _data.Length];
            return r;
        }
    }

}
