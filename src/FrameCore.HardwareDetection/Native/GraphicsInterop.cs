using System.Runtime.InteropServices;

namespace FrameCore.HardwareDetection.Native;

public sealed record DxgiAdapter(string Description, int VendorId, int DeviceId, int SubSysId, int Revision,
    long DedicatedVideoMemory, long DedicatedSystemMemory, long SharedSystemMemory, long Luid, bool IsSoftware);

public sealed record KmtAdapterInfo(long Luid, int? WddmVersion, bool? HagsSupported, bool? HagsEnabled, bool? HagsEnabledByDefault,
    bool? HybridDiscrete, bool? HybridIntegrated, bool? SoftwareDevice, bool? RenderSupported, bool? DisplaySupported);

/// <summary>DXGI adapter enumeration through raw COM vtables (no dependency), plus D3DKMT kernel thunks for WDDM caps.</summary>
public static unsafe class GraphicsInterop
{
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    public static List<DxgiAdapter> EnumerateDxgi()
    {
        var list = new List<DxgiAdapter>();
        if (!OperatingSystem.IsWindows()) return list;
        if (CreateDXGIFactory1(IID_IDXGIFactory1, out var factory) < 0 || factory == IntPtr.Zero) return list;
        try
        {
            var vtbl = *(IntPtr**)factory;
            // IDXGIFactory1::EnumAdapters1 is slot 12 (IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5).
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtbl[12];
            for (uint i = 0; i < 16; i++)
            {
                if (enumAdapters1(factory, i, out var adapter) < 0 || adapter == IntPtr.Zero) break;
                try
                {
                    var avt = *(IntPtr**)adapter;
                    // IDXGIAdapter1::GetDesc1 is slot 10 (IUnknown 3 + IDXGIObject 4 + IDXGIAdapter 3).
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)avt[10];
                    var size = Marshal.SizeOf<DXGI_ADAPTER_DESC1>();
                    var buf = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (getDesc1(adapter, buf) >= 0)
                        {
                            var d = Marshal.PtrToStructure<DXGI_ADAPTER_DESC1>(buf);
                            list.Add(new DxgiAdapter(d.Description.Trim(), (int)d.VendorId, (int)d.DeviceId, (int)d.SubSysId, (int)d.Revision,
                                (long)d.DedicatedVideoMemory, (long)d.DedicatedSystemMemory, (long)d.SharedSystemMemory,
                                ((long)d.LuidHigh << 32) | d.LuidLow, (d.Flags & 2) != 0));
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
        return list;
    }

    private static void Release(IntPtr unk)
    {
        var vt = *(IntPtr**)unk;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vt[2])(unk);
    }

    // ---- D3DKMT -------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);

    private const int KMTQAITYPE_DRIVERVERSION = 13, KMTQAITYPE_ADAPTERTYPE = 15, KMTQAITYPE_WDDM_2_7_CAPS = 70;

    private static uint? QueryUInt(uint hAdapter, int type)
    {
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buf, 0);
            var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = hAdapter, Type = type, pPrivateDriverData = buf, PrivateDriverDataSize = 4 };
            return D3DKMTQueryAdapterInfo(ref q) == 0 ? (uint)Marshal.ReadInt32(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static List<KmtAdapterInfo> EnumerateKmt()
    {
        var result = new List<KmtAdapterInfo>();
        if (!OperatingSystem.IsWindows()) return result;
        try
        {
            var e = new D3DKMT_ENUMADAPTERS2();
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return result;
            var size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
            e.pAdapters = Marshal.AllocHGlobal(size * (int)e.NumAdapters);
            try
            {
                if (D3DKMTEnumAdapters2(ref e) != 0) return result;
                for (var i = 0; i < e.NumAdapters; i++)
                {
                    var a = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(e.pAdapters + i * size);
                    var wddm = QueryUInt(a.hAdapter, KMTQAITYPE_DRIVERVERSION);
                    var caps = QueryUInt(a.hAdapter, KMTQAITYPE_WDDM_2_7_CAPS);
                    var type = QueryUInt(a.hAdapter, KMTQAITYPE_ADAPTERTYPE);
                    result.Add(new KmtAdapterInfo(((long)a.LuidHigh << 32) | a.LuidLow, (int?)wddm,
                        caps.HasValue ? (caps & 1) != 0 : null, caps.HasValue ? (caps & 2) != 0 : null, caps.HasValue ? (caps & 4) != 0 : null,
                        type.HasValue ? (type & 16) != 0 : null, type.HasValue ? (type & 32) != 0 : null,
                        type.HasValue ? (type & 4) != 0 : null, type.HasValue ? (type & 1) != 0 : null, type.HasValue ? (type & 2) != 0 : null));
                    var c = new D3DKMT_CLOSEADAPTER { hAdapter = a.hAdapter };
                    D3DKMTCloseAdapter(ref c);
                }
            }
            finally { Marshal.FreeHGlobal(e.pAdapters); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return result;
    }
}
