using System.Globalization;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Platform;
using FrameCore.GpuManager.Amd;
using FrameCore.GpuManager.Nvidia;

namespace FrameCore.GpuManager;

public sealed class GpuSample
{
    public DateTime Time { get; set; } = DateTime.Now;
    public long Luid { get; set; }
    public string Name { get; set; } = "";
    public double? UtilizationPercent { get; set; }
    public Dictionary<string, double> EngineUtilization { get; set; } = new();
    public long? DedicatedUsedBytes { get; set; }
    public long? DedicatedTotalBytes { get; set; }
    public double? TemperatureC { get; set; }
    public double? HotspotC { get; set; }
    public double? CoreClockMhz { get; set; }
    public double? MemoryClockMhz { get; set; }
    public double? PowerW { get; set; }
    public double? PowerLimitW { get; set; }
    public double? FanPercent { get; set; }
    public List<string> LimitReasons { get; set; } = new();
}

/// <summary>
/// Vendor-neutral GPU monitoring via WDDM performance counters (GPU Engine / GPU Adapter Memory), enriched with NVML
/// on NVIDIA. AMD/Intel temperatures, clocks and power come from LibreHardwareMonitor sensors when available.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private readonly PdhQuery _pdh = new();
    private readonly Nvml? _nvml;
    private readonly List<(IntPtr handle, string name)> _nvDevices = new();
    private static readonly Regex LuidRegex = new(@"luid_0x([0-9A-Fa-f]{8})_0x([0-9A-Fa-f]{8})", RegexOptions.Compiled);
    private static readonly Regex EngRegex = new(@"engtype_([A-Za-z0-9 ]+)$", RegexOptions.Compiled);

    public GpuMonitor()
    {
        _pdh.Add("engine", @"\GPU Engine(*)\Utilization Percentage");
        _pdh.Add("dedicated", @"\GPU Adapter Memory(*)\Dedicated Usage");
        _pdh.Collect();
        _nvml = Nvml.TryOpen();
        if (_nvml != null)
            for (var i = 0; i < _nvml.DeviceCount(); i++)
            {
                var h = _nvml.Device(i);
                if (h != IntPtr.Zero) _nvDevices.Add((h, _nvml.Name(h) ?? "NVIDIA"));
            }
    }

    public bool NvmlAvailable => _nvml != null;

    public static long ParseLuid(string instance)
    {
        var m = LuidRegex.Match(instance);
        if (!m.Success) return 0;
        var high = long.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var low = long.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (high << 32) | low;
    }

    /// <summary>Per adapter: utilization of the busiest engine type (same method as Task Manager).</summary>
    public static Dictionary<long, Dictionary<string, double>> AggregateEngines(Dictionary<string, double> raw)
    {
        var result = new Dictionary<long, Dictionary<string, double>>();
        foreach (var (instance, value) in raw)
        {
            var luid = ParseLuid(instance);
            var eng = EngRegex.Match(instance);
            var type = eng.Success ? eng.Groups[1].Value : "Other";
            if (!result.TryGetValue(luid, out var d)) result[luid] = d = new(StringComparer.OrdinalIgnoreCase);
            d[type] = d.GetValueOrDefault(type) + value;
        }
        return result;
    }

    public List<GpuSample> Read(IReadOnlyList<GpuInfo> gpus)
    {
        _pdh.Collect();
        var engines = AggregateEngines(_pdh.GetArray("engine"));
        var dedicated = _pdh.GetArray("dedicated").GroupBy(kv => ParseLuid(kv.Key)).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        var samples = new List<GpuSample>();
        foreach (var g in gpus.Where(x => !x.IsSoftware))
        {
            var s = new GpuSample { Luid = g.Luid, Name = g.Name, DedicatedTotalBytes = g.DedicatedVramBytes };
            if (engines.TryGetValue(g.Luid, out var e))
            {
                s.EngineUtilization = e.ToDictionary(kv => kv.Key, kv => Math.Min(100, kv.Value));
                s.UtilizationPercent = s.EngineUtilization.Count > 0 ? s.EngineUtilization.Values.Max() : null;
            }
            if (dedicated.TryGetValue(g.Luid, out var used)) s.DedicatedUsedBytes = (long)used;
            if (g.Vendor == GpuVendor.Nvidia && _nvml != null) FillNvml(s, g);
            samples.Add(s);
        }
        return samples;
    }

    private void FillNvml(GpuSample s, GpuInfo g)
    {
        var dev = _nvDevices.FirstOrDefault(d => NamesMatch(d.name, g.Name)).handle;
        if (dev == IntPtr.Zero && _nvDevices.Count == 1) dev = _nvDevices[0].handle;
        if (dev == IntPtr.Zero) return;
        s.TemperatureC = _nvml!.TemperatureC(dev);
        s.CoreClockMhz = _nvml.ClockMHz(dev, Nvml.CLOCK_GRAPHICS);
        s.MemoryClockMhz = _nvml.ClockMHz(dev, Nvml.CLOCK_MEM);
        s.PowerW = _nvml.PowerUsageMw(dev) / 1000.0;
        s.PowerLimitW = _nvml.EnforcedPowerLimitMw(dev) / 1000.0;
        s.FanPercent = _nvml.FanPercent(dev);
        if (_nvml.GetUtilization(dev) is { } u && s.UtilizationPercent == null) s.UtilizationPercent = u.Gpu;
        if (_nvml.GetMemory(dev) is { } m) { s.DedicatedUsedBytes ??= (long)m.Used; s.DedicatedTotalBytes = (long)m.Total; }
        if (_nvml.ClockEventReasons(dev) is { } r) s.LimitReasons = Nvml.DescribeClockEvents(r).ToList();
    }

    public static bool NamesMatch(string a, string b) =>
        Normalize(a).Contains(Normalize(b), StringComparison.OrdinalIgnoreCase) || Normalize(b).Contains(Normalize(a), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string s) => s.Replace("NVIDIA", "", StringComparison.OrdinalIgnoreCase).Replace("GeForce", "", StringComparison.OrdinalIgnoreCase).Trim();

    public void Dispose()
    {
        _pdh.Dispose();
        _nvml?.Dispose();
    }
}

/// <summary>Adds vendor-API information (VBIOS, PCIe link, Resizable BAR via BAR1) to the detected GPUs.</summary>
public sealed class GpuVendorEnricher : IHardwareEnricher
{
    public string Name => Loc.T("scan.gpuVendor");

    public void Enrich(MachineProfile profile)
    {
        if (profile.Gpus.Any(g => g.Vendor == GpuVendor.Nvidia)) EnrichNvidia(profile);
        if (profile.Gpus.Any(g => g.Vendor == GpuVendor.Amd && !g.IsIntegrated)) EnrichAmd(profile);
    }

    private static void EnrichNvidia(MachineProfile p)
    {
        using var nvml = Nvml.TryOpen();
        if (nvml == null) { p.Warnings.Add(Loc.T("gpu.nvml.unavailable")); return; }
        for (var i = 0; i < nvml.DeviceCount(); i++)
        {
            var dev = nvml.Device(i);
            var name = nvml.Name(dev) ?? "";
            var g = p.Gpus.FirstOrDefault(x => x.Vendor == GpuVendor.Nvidia && GpuMonitor.NamesMatch(x.Name, name));
            if (g == null) continue;
            g.VbiosVersion = nvml.VbiosVersion(dev);
            g.PcieCurrentGen = (int?)nvml.PcieGen(dev);
            g.PcieCurrentWidth = (int?)nvml.PcieWidth(dev);
            g.PcieMaxGen = (int?)nvml.PcieMaxGen(dev);
            g.PcieMaxWidth = (int?)nvml.PcieMaxWidth(dev);
            if (nvml.GetBar1(dev) is { } bar && nvml.GetMemory(dev) is { } mem)
            {
                // BAR1 ≈ VRAM size means Resizable BAR is active; 256 MB means it is not.
                if (bar.Total >= mem.Total * 0.9) { g.ResizableBar = ResizableBarState.Enabled; g.ResizableBarDetail = Loc.T("gpu.rebar.bar1", bar.Total / 1048576); }
                else if (bar.Total <= 512UL * 1048576) { g.ResizableBar = ResizableBarState.Disabled; g.ResizableBarDetail = Loc.T("gpu.rebar.bar1", bar.Total / 1048576); }
            }
        }
    }

    private static void EnrichAmd(MachineProfile p)
    {
        using var adlx = Adlx.TryOpen();
        if (adlx == null) { p.Warnings.Add(Loc.T("gpu.adlx.unavailable")); return; }
        foreach (var gpu in adlx.GetGpus())
        {
            try
            {
                var name = Adlx.GpuName(gpu) ?? "";
                var g = p.Gpus.FirstOrDefault(x => x.Vendor == GpuVendor.Amd && GpuMonitor.NamesMatch(x.Name, name));
                if (g != null) g.VbiosVersion ??= Adlx.BiosVersion(gpu);
            }
            finally { Adlx.Release(gpu); }
        }
    }
}
