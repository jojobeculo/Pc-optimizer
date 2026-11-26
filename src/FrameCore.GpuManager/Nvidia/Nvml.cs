using System.Runtime.InteropServices;
using System.Text;

namespace FrameCore.GpuManager.Nvidia;

/// <summary>
/// NVIDIA Management Library (nvml.dll, installed with the driver). Documented API; reads work as a standard
/// user, writes (clock offsets, power limit) need administrator rights and are bounded by driver-reported ranges.
/// Signatures follow nvml.h (NVIDIA/go-nvml copy, 2026).
/// </summary>
public sealed unsafe class Nvml : IDisposable
{
    public const int NVML_SUCCESS = 0, NVML_ERROR_NOT_SUPPORTED = 3, NVML_ERROR_NO_PERMISSION = 4, NVML_ERROR_INVALID_ARGUMENT = 2, NVML_ERROR_FUNCTION_NOT_FOUND = 13;
    public const int CLOCK_GRAPHICS = 0, CLOCK_SM = 1, CLOCK_MEM = 2;
    public const uint ClockOffsetV1Version = 24u | (1u << 24);

    private IntPtr _lib;
    private bool _initialized;

    [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] public struct MemoryInfo { public ulong Total, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] public struct Bar1Memory { public ulong Total, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] public struct ClockOffset { public uint Version; public int Type; public int PState; public int OffsetMHz, MinMHz, MaxMHz; }

    public static string? FindLibrary()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static Nvml? TryOpen()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = FindLibrary();
        if (path == null || !NativeLibrary.TryLoad(path, out var lib)) return null;
        var n = new Nvml { _lib = lib };
        var init = (delegate* unmanaged<int>)n.Export("nvmlInit_v2");
        if (init == null || init() != NVML_SUCCESS) { n.Dispose(); return null; }
        n._initialized = true;
        return n;
    }

    private IntPtr Export(string name) => NativeLibrary.TryGetExport(_lib, name, out var p) ? p : IntPtr.Zero;

    public bool Has(string name) => Export(name) != IntPtr.Zero;

    public static string ErrorText(int code) => code switch
    {
        0 => "OK", 1 => "UNINITIALIZED", 2 => "INVALID_ARGUMENT", 3 => "NOT_SUPPORTED", 4 => "NO_PERMISSION", 6 => "NOT_FOUND",
        13 => "FUNCTION_NOT_FOUND", 15 => "GPU_IS_LOST", 999 => "UNKNOWN", _ => "NVML error " + code,
    };

    public int DeviceCount()
    {
        var f = (delegate* unmanaged<uint*, int>)Export("nvmlDeviceGetCount_v2");
        uint c = 0;
        return f != null && f(&c) == NVML_SUCCESS ? (int)c : 0;
    }

    public IntPtr Device(int index)
    {
        var f = (delegate* unmanaged<uint, IntPtr*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
        IntPtr h = IntPtr.Zero;
        return f != null && f((uint)index, &h) == NVML_SUCCESS ? h : IntPtr.Zero;
    }

    private string? Str(string fn, IntPtr dev, int len = 96)
    {
        var f = (delegate* unmanaged<IntPtr, byte*, uint, int>)Export(fn);
        if (f == null) return null;
        var buf = stackalloc byte[len];
        return f(dev, buf, (uint)len) == NVML_SUCCESS ? Encoding.ASCII.GetString(buf, len).Split('\0')[0] : null;
    }

    private uint? U(string fn, IntPtr dev)
    {
        var f = (delegate* unmanaged<IntPtr, uint*, int>)Export(fn);
        uint v = 0;
        return f != null && f(dev, &v) == NVML_SUCCESS ? v : null;
    }

    private uint? U(string fn, IntPtr dev, int arg)
    {
        var f = (delegate* unmanaged<IntPtr, int, uint*, int>)Export(fn);
        uint v = 0;
        return f != null && f(dev, arg, &v) == NVML_SUCCESS ? v : null;
    }

    public string? Name(IntPtr d) => Str("nvmlDeviceGetName", d);
    public string? VbiosVersion(IntPtr d) => Str("nvmlDeviceGetVbiosVersion", d, 32);
    public string? Uuid(IntPtr d) => Str("nvmlDeviceGetUUID", d, 96);

    public string? DriverVersion()
    {
        var f = (delegate* unmanaged<byte*, uint, int>)Export("nvmlSystemGetDriverVersion");
        if (f == null) return null;
        var buf = stackalloc byte[96];
        return f(buf, 96) == NVML_SUCCESS ? Encoding.ASCII.GetString(buf, 96).Split('\0')[0] : null;
    }

    public uint? TemperatureC(IntPtr d) => U("nvmlDeviceGetTemperature", d, 0);
    public uint? SlowdownTemperatureC(IntPtr d) => U("nvmlDeviceGetTemperatureThreshold", d, 1);
    public uint? ClockMHz(IntPtr d, int type) => U("nvmlDeviceGetClockInfo", d, type);
    public uint? MaxClockMHz(IntPtr d, int type) => U("nvmlDeviceGetMaxClockInfo", d, type);
    public uint? PowerUsageMw(IntPtr d) => U("nvmlDeviceGetPowerUsage", d);
    public uint? EnforcedPowerLimitMw(IntPtr d) => U("nvmlDeviceGetEnforcedPowerLimit", d);
    public uint? DefaultPowerLimitMw(IntPtr d) => U("nvmlDeviceGetPowerManagementDefaultLimit", d);
    public uint? PowerLimitMw(IntPtr d) => U("nvmlDeviceGetPowerManagementLimit", d);
    public uint? FanPercent(IntPtr d) => U("nvmlDeviceGetFanSpeed", d);
    public uint? PcieGen(IntPtr d) => U("nvmlDeviceGetCurrPcieLinkGeneration", d);
    public uint? PcieWidth(IntPtr d) => U("nvmlDeviceGetCurrPcieLinkWidth", d);
    public uint? PcieMaxGen(IntPtr d) => U("nvmlDeviceGetMaxPcieLinkGeneration", d);
    public uint? PcieMaxWidth(IntPtr d) => U("nvmlDeviceGetMaxPcieLinkWidth", d);

    public (uint min, uint max)? PowerLimitConstraints(IntPtr d)
    {
        var f = (delegate* unmanaged<IntPtr, uint*, uint*, int>)Export("nvmlDeviceGetPowerManagementLimitConstraints");
        uint a = 0, b = 0;
        return f != null && f(d, &a, &b) == NVML_SUCCESS ? (a, b) : null;
    }

    public Utilization? GetUtilization(IntPtr d)
    {
        var f = (delegate* unmanaged<IntPtr, Utilization*, int>)Export("nvmlDeviceGetUtilizationRates");
        Utilization u;
        return f != null && f(d, &u) == NVML_SUCCESS ? u : null;
    }

    public MemoryInfo? GetMemory(IntPtr d)
    {
        var f = (delegate* unmanaged<IntPtr, MemoryInfo*, int>)Export("nvmlDeviceGetMemoryInfo");
        MemoryInfo m;
        return f != null && f(d, &m) == NVML_SUCCESS ? m : null;
    }

    public Bar1Memory? GetBar1(IntPtr d)
    {
        var f = (delegate* unmanaged<IntPtr, Bar1Memory*, int>)Export("nvmlDeviceGetBAR1MemoryInfo");
        Bar1Memory m;
        return f != null && f(d, &m) == NVML_SUCCESS ? m : null;
    }

    public ulong? ClockEventReasons(IntPtr d)
    {
        var f = (delegate* unmanaged<IntPtr, ulong*, int>)Export("nvmlDeviceGetCurrentClocksEventReasons");
        if (f == null) f = (delegate* unmanaged<IntPtr, ulong*, int>)Export("nvmlDeviceGetCurrentClocksThrottleReasons");
        ulong r = 0;
        return f != null && f(d, &r) == NVML_SUCCESS ? r : null;
    }

    /// <summary>Clock offset (P0) with its allowed range: new API (R555+) with fallback to the older VfOffset calls.</summary>
    public (int offset, int min, int max, int rc) GetClockOffset(IntPtr d, int clockType)
    {
        var f = (delegate* unmanaged<IntPtr, ClockOffset*, int>)Export("nvmlDeviceGetClockOffsets");
        if (f != null)
        {
            var info = new ClockOffset { Version = ClockOffsetV1Version, Type = clockType, PState = 0 };
            var rc = f(d, &info);
            if (rc == NVML_SUCCESS) return (info.OffsetMHz, info.MinMHz, info.MaxMHz, rc);
        }
        var getName = clockType == CLOCK_MEM ? "nvmlDeviceGetMemClkVfOffset" : "nvmlDeviceGetGpcClkVfOffset";
        var rangeName = clockType == CLOCK_MEM ? "nvmlDeviceGetMemClkMinMaxVfOffset" : "nvmlDeviceGetGpcClkMinMaxVfOffset";
        var g = (delegate* unmanaged<IntPtr, int*, int>)Export(getName);
        var r = (delegate* unmanaged<IntPtr, int*, int*, int>)Export(rangeName);
        if (g == null || r == null) return (0, 0, 0, NVML_ERROR_FUNCTION_NOT_FOUND);
        int off = 0, mn = 0, mx = 0;
        var rc1 = g(d, &off);
        var rc2 = r(d, &mn, &mx);
        return (off, mn, mx, rc1 != NVML_SUCCESS ? rc1 : rc2);
    }

    public int SetClockOffset(IntPtr d, int clockType, int offsetMHz)
    {
        var f = (delegate* unmanaged<IntPtr, ClockOffset*, int>)Export("nvmlDeviceSetClockOffsets");
        if (f != null)
        {
            var info = new ClockOffset { Version = ClockOffsetV1Version, Type = clockType, PState = 0, OffsetMHz = offsetMHz };
            var rc = f(d, &info);
            if (rc != NVML_ERROR_FUNCTION_NOT_FOUND && rc != NVML_ERROR_NOT_SUPPORTED) return rc;
        }
        var old = (delegate* unmanaged<IntPtr, int, int>)Export(clockType == CLOCK_MEM ? "nvmlDeviceSetMemClkVfOffset" : "nvmlDeviceSetGpcClkVfOffset");
        return old == null ? NVML_ERROR_FUNCTION_NOT_FOUND : old(d, offsetMHz);
    }

    public int SetPowerLimitMw(IntPtr d, uint mw)
    {
        var f = (delegate* unmanaged<IntPtr, uint, int>)Export("nvmlDeviceSetPowerManagementLimit");
        return f == null ? NVML_ERROR_FUNCTION_NOT_FOUND : f(d, mw);
    }

    public static IEnumerable<string> DescribeClockEvents(ulong reasons)
    {
        if ((reasons & 0x4) != 0) yield return "power";
        if ((reasons & 0x20) != 0 || (reasons & 0x40) != 0) yield return "thermal";
        if ((reasons & 0x8) != 0) yield return "hw-slowdown";
        if ((reasons & 0x80) != 0) yield return "power-brake";
        if ((reasons & 0x200) != 0) yield return "board-limit";
        if ((reasons & 0x400) != 0) yield return "reliability";
        if ((reasons & 0x2) != 0) yield return "app-clocks";
    }

    public void Dispose()
    {
        if (_initialized)
        {
            var shutdown = (delegate* unmanaged<int>)Export("nvmlShutdown");
            if (shutdown != null) shutdown();
            _initialized = false;
        }
        if (_lib != IntPtr.Zero) { NativeLibrary.Free(_lib); _lib = IntPtr.Zero; }
    }
}
