using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.GpuManager.Amd;
using FrameCore.GpuManager.Nvidia;

namespace FrameCore.GpuManager;

public sealed class GpuTuningCapabilities
{
    public int Index { get; set; }
    public string Vendor { get; set; } = "";
    public string GpuName { get; set; } = "";
    public bool Available { get; set; }
    public string? UnavailableReason { get; set; }
    public bool CoreOffset { get; set; }
    public int CoreMin { get; set; }
    public int CoreMax { get; set; }
    public bool MemOffset { get; set; }
    public int MemMin { get; set; }
    public int MemMax { get; set; }
    public bool PowerLimit { get; set; }
    public double PowerMin { get; set; }
    public double PowerMax { get; set; }
    public double? PowerDefault { get; set; }
    public string PowerUnit { get; set; } = "W";
    public bool AutoUndervolt { get; set; }
    public bool AutoOverclockGpu { get; set; }
    public bool AutoOverclockVram { get; set; }
    public bool ResetToFactory { get; set; }
    public List<string> Notes { get; set; } = new();
    public string? OfficialTool { get; set; }
    public string? OfficialToolUrl { get; set; }
}

public sealed class GpuTuningState
{
    public int Index { get; set; }
    public int? CoreOffsetMhz { get; set; }
    public int? MemOffsetMhz { get; set; }
    public double? PowerLimit { get; set; }
    public string? ActivePreset { get; set; }
}

public sealed class GpuPresetRequest
{
    public int Index { get; set; }
    /// <summary>undervoltGpu | overclockGpu | overclockVram</summary>
    public string Preset { get; set; } = "";
}

/// <summary>Vendor tuning adapter. Every write is bounded by the ranges the vendor API itself reports; no voltage control.</summary>
public interface IGpuTuningAdapter : IDisposable
{
    string Vendor { get; }
    IReadOnlyList<GpuTuningCapabilities> Capabilities();
    GpuTuningState Read(int index);
    void Apply(GpuTuningState target);
    void Reset(int index);
    bool RunPreset(GpuPresetRequest request);
}

public sealed class NvidiaTuningAdapter : IGpuTuningAdapter
{
    private readonly Nvml _nvml;
    public NvidiaTuningAdapter(Nvml nvml) => _nvml = nvml;
    public string Vendor => "NVIDIA";

    public IReadOnlyList<GpuTuningCapabilities> Capabilities()
    {
        var list = new List<GpuTuningCapabilities>();
        for (var i = 0; i < _nvml.DeviceCount(); i++)
        {
            var d = _nvml.Device(i);
            var caps = new GpuTuningCapabilities
            {
                Index = i, Vendor = Vendor, GpuName = _nvml.Name(d) ?? "NVIDIA GPU", OfficialTool = "NVIDIA App",
                OfficialToolUrl = "https://www.nvidia.com/en-us/software/nvidia-app/",
            };
            var core = _nvml.GetClockOffset(d, Nvml.CLOCK_GRAPHICS);
            caps.CoreOffset = core.rc == Nvml.NVML_SUCCESS && core.max > core.min;
            caps.CoreMin = core.min; caps.CoreMax = core.max;
            var mem = _nvml.GetClockOffset(d, Nvml.CLOCK_MEM);
            caps.MemOffset = mem.rc == Nvml.NVML_SUCCESS && mem.max > mem.min;
            caps.MemMin = mem.min; caps.MemMax = mem.max;
            if (_nvml.PowerLimitConstraints(d) is { } pl && pl.max > pl.min)
            {
                caps.PowerLimit = true; caps.PowerMin = pl.min / 1000.0; caps.PowerMax = pl.max / 1000.0;
                caps.PowerDefault = _nvml.DefaultPowerLimitMw(d) / 1000.0;
            }
            caps.Available = caps.CoreOffset || caps.MemOffset || caps.PowerLimit;
            if (!caps.Available) caps.UnavailableReason = Loc.T("gpu.tuning.nvmlNotSupported", Nvml.ErrorText(core.rc));
            caps.Notes.Add(Loc.T("gpu.tuning.nvmlNotes"));
            caps.Notes.Add(Loc.T("gpu.tuning.memScale"));
            list.Add(caps);
        }
        return list;
    }

    public GpuTuningState Read(int index)
    {
        var d = _nvml.Device(index);
        var core = _nvml.GetClockOffset(d, Nvml.CLOCK_GRAPHICS);
        var mem = _nvml.GetClockOffset(d, Nvml.CLOCK_MEM);
        return new GpuTuningState
        {
            Index = index,
            CoreOffsetMhz = core.rc == 0 ? core.offset : null,
            MemOffsetMhz = mem.rc == 0 ? mem.offset : null,
            PowerLimit = _nvml.PowerLimitMw(d) / 1000.0,
        };
    }

    public void Apply(GpuTuningState t)
    {
        var caps = Capabilities().First(c => c.Index == t.Index);
        var d = _nvml.Device(t.Index);
        if (t.CoreOffsetMhz is { } core)
        {
            if (!caps.CoreOffset || core < caps.CoreMin || core > caps.CoreMax) throw new ArgumentOutOfRangeException(nameof(t.CoreOffsetMhz), Loc.T("gpu.tuning.outOfRange", core, caps.CoreMin, caps.CoreMax));
            Check(_nvml.SetClockOffset(d, Nvml.CLOCK_GRAPHICS, core));
        }
        if (t.MemOffsetMhz is { } mem)
        {
            if (!caps.MemOffset || mem < caps.MemMin || mem > caps.MemMax) throw new ArgumentOutOfRangeException(nameof(t.MemOffsetMhz), Loc.T("gpu.tuning.outOfRange", mem, caps.MemMin, caps.MemMax));
            Check(_nvml.SetClockOffset(d, Nvml.CLOCK_MEM, mem));
        }
        if (t.PowerLimit is { } w)
        {
            if (!caps.PowerLimit || w < caps.PowerMin || w > caps.PowerMax) throw new ArgumentOutOfRangeException(nameof(t.PowerLimit), Loc.T("gpu.tuning.outOfRange", w, caps.PowerMin, caps.PowerMax));
            Check(_nvml.SetPowerLimitMw(d, (uint)Math.Round(w * 1000)));
        }
        Log.Info("GpuTuning", $"NVIDIA #{t.Index}: core {t.CoreOffsetMhz} MHz, mem {t.MemOffsetMhz} MHz, power {t.PowerLimit} W");
    }

    public void Reset(int index)
    {
        var caps = Capabilities().First(c => c.Index == index);
        Apply(new GpuTuningState
        {
            Index = index,
            CoreOffsetMhz = caps.CoreOffset ? 0 : null,
            MemOffsetMhz = caps.MemOffset ? 0 : null,
            PowerLimit = caps.PowerLimit ? caps.PowerDefault : null,
        });
    }

    public bool RunPreset(GpuPresetRequest request) => throw new NotSupportedException(Loc.T("gpu.tuning.nvidiaNoPreset"));

    private static void Check(int rc)
    {
        if (rc == Nvml.NVML_SUCCESS) return;
        if (rc == Nvml.NVML_ERROR_NO_PERMISSION) throw new UnauthorizedAccessException("NVML: " + Nvml.ErrorText(rc));
        throw new InvalidOperationException("NVML: " + Nvml.ErrorText(rc));
    }

    public void Dispose() => _nvml.Dispose();
}

public sealed class AmdTuningAdapter : IGpuTuningAdapter
{
    private readonly Adlx _adlx;
    public AmdTuningAdapter(Adlx adlx) => _adlx = adlx;
    public string Vendor => "AMD";

    private T WithGpu<T>(int index, Func<IntPtr, IntPtr, T> f)
    {
        var gpus = _adlx.GetGpus();
        var services = _adlx.TuningServices();
        try
        {
            if (index >= gpus.Count || services == IntPtr.Zero) throw new InvalidOperationException("ADLX GPU not found");
            return f(gpus[index], services);
        }
        finally
        {
            foreach (var g in gpus) Adlx.Release(g);
            Adlx.Release(services);
        }
    }

    public IReadOnlyList<GpuTuningCapabilities> Capabilities()
    {
        var list = new List<GpuTuningCapabilities>();
        var count = _adlx.GetGpus().Select(g => { Adlx.Release(g); return 0; }).Count();
        for (var i = 0; i < count; i++)
        {
            list.Add(WithGpu(i, (gpu, s) =>
            {
                var caps = new GpuTuningCapabilities
                {
                    Index = i, Vendor = Vendor, GpuName = Adlx.GpuName(gpu) ?? "AMD GPU", ResetToFactory = true,
                    OfficialTool = "AMD Software: Adrenalin Edition", OfficialToolUrl = "https://www.amd.com/en/products/software/adrenalin.html",
                };
                if (Adlx.IsSupportedAutoTuning(s, gpu) == true)
                {
                    var at = Adlx.GetAutoTuning(s, gpu);
                    if (at != IntPtr.Zero)
                    {
                        caps.AutoUndervolt = Adlx.AutoTuningFlag(at, 3) == true;
                        caps.AutoOverclockGpu = Adlx.AutoTuningFlag(at, 4) == true;
                        caps.AutoOverclockVram = Adlx.AutoTuningFlag(at, 5) == true;
                        Adlx.Release(at);
                    }
                }
                if (Adlx.IsSupportedManualPowerTuning(s, gpu) == true)
                {
                    var pt = Adlx.GetManualPowerTuning(s, gpu);
                    if (pt != IntPtr.Zero && Adlx.PowerLimitRange(pt) is { } r)
                    {
                        caps.PowerLimit = r.Max > r.Min; caps.PowerMin = r.Min; caps.PowerMax = r.Max; caps.PowerUnit = "%"; caps.PowerDefault = 0;
                    }
                    Adlx.Release(pt);
                }
                caps.Available = caps.AutoUndervolt || caps.AutoOverclockGpu || caps.AutoOverclockVram || caps.PowerLimit;
                if (!caps.Available) caps.UnavailableReason = Loc.T("gpu.tuning.adlxNotSupported");
                caps.Notes.Add(Loc.T("gpu.tuning.adlxNotes"));
                return caps;
            }));
        }
        return list;
    }

    public GpuTuningState Read(int index) => WithGpu(index, (gpu, s) =>
    {
        var st = new GpuTuningState { Index = index };
        var pt = Adlx.GetManualPowerTuning(s, gpu);
        if (pt != IntPtr.Zero) { st.PowerLimit = Adlx.PowerLimit(pt); Adlx.Release(pt); }
        var at = Adlx.GetAutoTuning(s, gpu);
        if (at != IntPtr.Zero)
        {
            st.ActivePreset = Adlx.AutoTuningFlag(at, 6) == true ? "undervoltGpu" : Adlx.AutoTuningFlag(at, 7) == true ? "overclockGpu" : Adlx.AutoTuningFlag(at, 8) == true ? "overclockVram" : null;
            Adlx.Release(at);
        }
        if (Adlx.IsAtFactory(s, gpu) == true) st.ActivePreset ??= "factory";
        return st;
    });

    public void Apply(GpuTuningState t)
    {
        if (t.CoreOffsetMhz != null || t.MemOffsetMhz != null) throw new NotSupportedException(Loc.T("gpu.tuning.amdManualClocks"));
        if (t.PowerLimit is not { } p) return;
        WithGpu(t.Index, (gpu, s) =>
        {
            var pt = Adlx.GetManualPowerTuning(s, gpu);
            if (pt == IntPtr.Zero) throw new NotSupportedException("ADLX power tuning unavailable");
            try
            {
                var r = Adlx.PowerLimitRange(pt) ?? throw new InvalidOperationException("No range");
                var value = (int)Math.Round(p);
                if (value < r.Min || value > r.Max) throw new ArgumentOutOfRangeException(nameof(t.PowerLimit), Loc.T("gpu.tuning.outOfRange", value, r.Min, r.Max));
                var rc = Adlx.SetPowerLimit(pt, value);
                if (!Adlx.Succeeded(rc)) throw new InvalidOperationException("ADLX SetPowerLimit: " + rc);
            }
            finally { Adlx.Release(pt); }
            return true;
        });
    }

    public void Reset(int index) => WithGpu(index, (gpu, s) =>
    {
        var rc = Adlx.ResetToFactory(s, gpu);
        if (!Adlx.Succeeded(rc)) throw new InvalidOperationException("ADLX ResetToFactory: " + rc);
        Log.Info("GpuTuning", $"AMD #{index}: reset to factory");
        return true;
    });

    public bool RunPreset(GpuPresetRequest request) => WithGpu(request.Index, (gpu, s) =>
    {
        var slot = request.Preset switch { "undervoltGpu" => 9, "overclockGpu" => 10, "overclockVram" => 11, _ => throw new ArgumentException(request.Preset) };
        var at = Adlx.GetAutoTuning(s, gpu);
        if (at == IntPtr.Zero) throw new NotSupportedException("ADLX auto-tuning unavailable");
        try
        {
            var (rc, ok) = Adlx.StartAutoTuning(at, slot, TimeSpan.FromMinutes(10));
            Log.Info("GpuTuning", $"AMD #{request.Index}: preset {request.Preset} rc={rc} completed={ok}");
            if (!Adlx.Succeeded(rc)) throw new InvalidOperationException("ADLX auto-tuning: " + rc);
            return ok;
        }
        finally { Adlx.Release(at); }
    });

    public void Dispose() => _adlx.Dispose();
}

/// <summary>Creates the adapters available on this machine (run inside the elevated worker).</summary>
public sealed class GpuTuningService : IDisposable
{
    private readonly List<IGpuTuningAdapter> _adapters = new();

    public GpuTuningService()
    {
        if (Nvml.TryOpen() is { } nvml) _adapters.Add(new NvidiaTuningAdapter(nvml));
        if (Adlx.TryOpen() is { } adlx) _adapters.Add(new AmdTuningAdapter(adlx));
    }

    public IReadOnlyList<GpuTuningCapabilities> Capabilities() => _adapters.SelectMany(a =>
    {
        try { return a.Capabilities(); }
        catch (Exception ex) { Log.Error("GpuTuning", a.Vendor, ex); return Array.Empty<GpuTuningCapabilities>(); }
    }).ToList();

    private IGpuTuningAdapter For(string vendor) =>
        _adapters.FirstOrDefault(a => a.Vendor.Equals(vendor, StringComparison.OrdinalIgnoreCase)) ?? throw new NotSupportedException(vendor);

    public GpuTuningState Read(string vendor, int index) => For(vendor).Read(index);
    public void Apply(string vendor, GpuTuningState s) => For(vendor).Apply(s);
    public void Reset(string vendor, int index) => For(vendor).Reset(index);
    public bool RunPreset(string vendor, GpuPresetRequest r) => For(vendor).RunPreset(r);

    public void Dispose() { foreach (var a in _adapters) a.Dispose(); }
}

public sealed class GpuTuningRequest
{
    public string Vendor { get; set; } = "";
    public GpuTuningState State { get; set; } = new();
    public GpuPresetRequest? Preset { get; set; }
}
