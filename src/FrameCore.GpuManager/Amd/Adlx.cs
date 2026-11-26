using System.Runtime.InteropServices;

namespace FrameCore.GpuManager.Amd;

/// <summary>
/// Minimal AMD ADLX client calling the documented C ABI (vtables) of amdadlx64.dll, shipped with AMD Software.
/// Slot numbers come from the official headers (GPUOpen-LibrariesAndSDKs/ADLX/SDK/Include, C section):
///   IADLXSystem: 1 GetGPUs, 8 GetGPUTuningServices
///   IADLXInterface: 0 Acquire, 1 Release, 2 QueryInterface
///   IADLXGPUList: 3 Size, 11 At_GPUList
///   IADLXGPU: 7 Name, 11 TotalVRAM, 13 BIOSInfo, 14 DeviceId, 18 UniqueId
///   IADLXGPUTuningServices: 4 IsAtFactory, 5 ResetToFactory, 6 IsSupportedAutoTuning, 11 IsSupportedManualPowerTuning,
///                           12 GetAutoTuning, 17 GetManualPowerTuning
///   IADLXGPUAutoTuning: 3-5 IsSupported{UndervoltGPU,OverclockGPU,OverclockVRAM}, 6-8 IsCurrent…, 9-11 Start…
///   IADLXGPUAutoTuningCompleteEvent: 3-5 Is…Completed
///   IADLXManualPowerTuning: 3 GetPowerLimitRange, 4 GetPowerLimit, 5 SetPowerLimit
/// </summary>
public sealed unsafe class Adlx : IDisposable
{
    public const int ADLX_OK = 0, ADLX_ALREADY_ENABLED = 1, ADLX_ALREADY_INITIALIZED = 2, ADLX_NOT_SUPPORTED = 12;
    public static bool Succeeded(int r) => r is ADLX_OK or ADLX_ALREADY_ENABLED or ADLX_ALREADY_INITIALIZED;

    private IntPtr _lib;
    private IntPtr _system;

    [StructLayout(LayoutKind.Sequential)] public struct IntRange { public int Min, Max, Step; }

    public static Adlx? TryOpen()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!NativeLibrary.TryLoad("amdadlx64.dll", out var lib)) return null;
        var a = new Adlx { _lib = lib };
        try
        {
            var queryFull = (delegate* unmanaged<ulong*, int>)a.Export("ADLXQueryFullVersion");
            ulong version = 0;
            if (queryFull == null || queryFull(&version) != ADLX_OK) { a.Dispose(); return null; }
            // ADLXInitialize2 has an extra IADLMapping** out parameter; the older ADLXInitialize does not.
            var init2 = (delegate* unmanaged<ulong, IntPtr*, IntPtr*, int>)a.Export("ADLXInitialize2");
            IntPtr sys = IntPtr.Zero, mapping = IntPtr.Zero;
            int rc;
            if (init2 != null) rc = init2(version, &sys, &mapping);
            else
            {
                var init = (delegate* unmanaged<ulong, IntPtr*, int>)a.Export("ADLXInitialize");
                if (init == null) { a.Dispose(); return null; }
                rc = init(version, &sys);
            }
            if (!Succeeded(rc) || sys == IntPtr.Zero) { a.Dispose(); return null; }
            a._system = sys;
            return a;
        }
        catch { a.Dispose(); return null; }
    }

    private IntPtr Export(string name) => NativeLibrary.TryGetExport(_lib, name, out var p) ? p : IntPtr.Zero;
    private static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];

    public static void Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return;
        ((delegate* unmanaged<IntPtr, int>)Slot(obj, 1))(obj);
    }

    public static IntPtr QueryInterface(IntPtr obj, string iid)
    {
        IntPtr result = IntPtr.Zero;
        fixed (char* p = iid + "\0")
        {
            var rc = ((delegate* unmanaged<IntPtr, char*, IntPtr*, int>)Slot(obj, 2))(obj, p, &result);
            return Succeeded(rc) ? result : IntPtr.Zero;
        }
    }

    public List<IntPtr> GetGpus()
    {
        var list = new List<IntPtr>();
        IntPtr gpuList = IntPtr.Zero;
        if (!Succeeded(((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(_system, 1))(_system, &gpuList)) || gpuList == IntPtr.Zero) return list;
        var size = ((delegate* unmanaged<IntPtr, uint>)Slot(gpuList, 3))(gpuList);
        for (uint i = 0; i < size; i++)
        {
            IntPtr gpu = IntPtr.Zero;
            if (Succeeded(((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Slot(gpuList, 11))(gpuList, i, &gpu)) && gpu != IntPtr.Zero) list.Add(gpu);
        }
        Release(gpuList);
        return list;
    }

    public static string? GpuName(IntPtr gpu)
    {
        byte* name = null;
        return Succeeded(((delegate* unmanaged<IntPtr, byte**, int>)Slot(gpu, 7))(gpu, &name)) && name != null ? Marshal.PtrToStringUTF8((IntPtr)name) : null;
    }

    public static uint? TotalVramMb(IntPtr gpu)
    {
        uint mb = 0;
        return Succeeded(((delegate* unmanaged<IntPtr, uint*, int>)Slot(gpu, 11))(gpu, &mb)) ? mb : null;
    }

    public static string? BiosVersion(IntPtr gpu)
    {
        byte* part = null, ver = null, date = null;
        return Succeeded(((delegate* unmanaged<IntPtr, byte**, byte**, byte**, int>)Slot(gpu, 13))(gpu, &part, &ver, &date)) && ver != null
            ? Marshal.PtrToStringUTF8((IntPtr)ver) : null;
    }

    public static string? DeviceId(IntPtr gpu)
    {
        byte* id = null;
        return Succeeded(((delegate* unmanaged<IntPtr, byte**, int>)Slot(gpu, 14))(gpu, &id)) && id != null ? Marshal.PtrToStringUTF8((IntPtr)id) : null;
    }

    public IntPtr TuningServices()
    {
        IntPtr s = IntPtr.Zero;
        return Succeeded(((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(_system, 8))(_system, &s)) ? s : IntPtr.Zero;
    }

    public static bool? BoolCall(IntPtr services, int slot, IntPtr gpu)
    {
        byte b = 0;
        return Succeeded(((delegate* unmanaged<IntPtr, IntPtr, byte*, int>)Slot(services, slot))(services, gpu, &b)) ? b != 0 : null;
    }

    public static bool? IsAtFactory(IntPtr services, IntPtr gpu) => BoolCall(services, 4, gpu);
    public static int ResetToFactory(IntPtr services, IntPtr gpu) => ((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(services, 5))(services, gpu);
    public static bool? IsSupportedAutoTuning(IntPtr services, IntPtr gpu) => BoolCall(services, 6, gpu);
    public static bool? IsSupportedManualPowerTuning(IntPtr services, IntPtr gpu) => BoolCall(services, 11, gpu);

    public static IntPtr GetAutoTuning(IntPtr services, IntPtr gpu)
    {
        IntPtr unk = IntPtr.Zero;
        if (!Succeeded(((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(services, 12))(services, gpu, &unk)) || unk == IntPtr.Zero) return IntPtr.Zero;
        var at = QueryInterface(unk, "IADLXGPUAutoTuning");
        Release(unk);
        return at;
    }

    public static IntPtr GetManualPowerTuning(IntPtr services, IntPtr gpu)
    {
        IntPtr unk = IntPtr.Zero;
        if (!Succeeded(((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(services, 17))(services, gpu, &unk)) || unk == IntPtr.Zero) return IntPtr.Zero;
        var pt = QueryInterface(unk, "IADLXManualPowerTuning");
        Release(unk);
        return pt;
    }

    public static bool? AutoTuningFlag(IntPtr autoTuning, int slot)
    {
        byte b = 0;
        return Succeeded(((delegate* unmanaged<IntPtr, byte*, int>)Slot(autoTuning, slot))(autoTuning, &b)) ? b != 0 : null;
    }

    // Auto-tuning completion listener: a C struct whose first field points to a one-entry vtable.
    private static readonly ManualResetEventSlim Completed = new(false);
    private static volatile bool _lastCompletedOk;

    [UnmanagedCallersOnly]
    private static byte OnComplete(IntPtr self, IntPtr evt)
    {
        try
        {
            if (evt != IntPtr.Zero)
            {
                var a = ((delegate* unmanaged<IntPtr, byte>)Slot(evt, 3))(evt);
                var b = ((delegate* unmanaged<IntPtr, byte>)Slot(evt, 4))(evt);
                var c = ((delegate* unmanaged<IntPtr, byte>)Slot(evt, 5))(evt);
                _lastCompletedOk = a != 0 || b != 0 || c != 0;
            }
        }
        catch { _lastCompletedOk = false; }
        Completed.Set();
        return 1;
    }

    /// <summary>Starts an official AMD auto-tuning preset (slot 9 undervolt GPU, 10 overclock GPU, 11 overclock VRAM) and waits.</summary>
    public static (int rc, bool completed) StartAutoTuning(IntPtr autoTuning, int slot, TimeSpan timeout)
    {
        var vtbl = (IntPtr*)NativeMemory.Alloc((nuint)IntPtr.Size);
        var listener = (IntPtr*)NativeMemory.Alloc((nuint)IntPtr.Size);
        try
        {
            vtbl[0] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&OnComplete;
            listener[0] = (IntPtr)vtbl;
            Completed.Reset();
            _lastCompletedOk = false;
            var rc = ((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(autoTuning, slot))(autoTuning, (IntPtr)listener);
            if (!Succeeded(rc)) return (rc, false);
            var done = Completed.Wait(timeout);
            return (rc, done && _lastCompletedOk);
        }
        finally
        {
            // Keep memory alive until the callback has fired or timed out.
            NativeMemory.Free(listener);
            NativeMemory.Free(vtbl);
        }
    }

    public static IntRange? PowerLimitRange(IntPtr powerTuning)
    {
        IntRange r;
        return Succeeded(((delegate* unmanaged<IntPtr, IntRange*, int>)Slot(powerTuning, 3))(powerTuning, &r)) ? r : null;
    }

    public static int? PowerLimit(IntPtr powerTuning)
    {
        int v = 0;
        return Succeeded(((delegate* unmanaged<IntPtr, int*, int>)Slot(powerTuning, 4))(powerTuning, &v)) ? v : null;
    }

    public static int SetPowerLimit(IntPtr powerTuning, int value) => ((delegate* unmanaged<IntPtr, int, int>)Slot(powerTuning, 5))(powerTuning, value);

    public void Dispose()
    {
        if (_system != IntPtr.Zero)
        {
            var term = (delegate* unmanaged<int>)Export("ADLXTerminate");
            if (term != null) term();
            _system = IntPtr.Zero;
        }
        if (_lib != IntPtr.Zero) { NativeLibrary.Free(_lib); _lib = IntPtr.Zero; }
    }
}
