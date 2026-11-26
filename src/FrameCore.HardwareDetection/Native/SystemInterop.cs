using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace FrameCore.HardwareDetection.Native;

public sealed record CoreTopology(int Cores, int LogicalProcessors, int PerformanceCores, int EfficiencyCores, bool Hybrid,
    List<long> L3CacheSizes, long L2TotalBytes);

public static class SystemInterop
{
    // ---- CPUID ---------------------------------------------------------------------------------
    public static bool CpuIdAvailable => X86Base.IsSupported;

    public static (int a, int b, int c, int d) CpuId(int leaf, int sub = 0)
    {
        if (!X86Base.IsSupported) return (0, 0, 0, 0);
        var r = X86Base.CpuId(leaf, sub);
        return (r.Eax, r.Ebx, r.Ecx, r.Edx);
    }

    public static string CpuVendorString()
    {
        var (_, b, c, d) = CpuId(0);
        var bytes = new byte[12];
        BitConverter.GetBytes(b).CopyTo(bytes, 0);
        BitConverter.GetBytes(d).CopyTo(bytes, 4);
        BitConverter.GetBytes(c).CopyTo(bytes, 8);
        return Encoding.ASCII.GetString(bytes);
    }

    public static string? CpuBrandString()
    {
        var (maxExt, _, _, _) = CpuId(unchecked((int)0x80000000));
        if ((uint)maxExt < 0x80000004) return null;
        var sb = new StringBuilder();
        for (uint leaf = 0x80000002; leaf <= 0x80000004; leaf++)
        {
            var (a, b, c, d) = CpuId(unchecked((int)leaf));
            foreach (var v in new[] { a, b, c, d }) sb.Append(Encoding.ASCII.GetString(BitConverter.GetBytes(v)));
        }
        return sb.ToString().Trim('\0', ' ');
    }

    /// <summary>Family/model/stepping with extended fields applied (Intel/AMD rules).</summary>
    public static (int family, int model, int stepping) CpuSignature()
    {
        var (a, _, _, _) = CpuId(1);
        var stepping = a & 0xF;
        var model = (a >> 4) & 0xF;
        var family = (a >> 8) & 0xF;
        var extModel = (a >> 16) & 0xF;
        var extFamily = (a >> 20) & 0xFF;
        if (family == 0xF) family += extFamily;
        if (family == 0x6 || family >= 0xF) model += extModel << 4;
        return (family, model, stepping);
    }

    public static List<string> CpuFeatures()
    {
        var f = new List<string>();
        if (!X86Base.IsSupported) return f;
        var (_, _, c1, d1) = CpuId(1);
        var (_, b7, _, d7) = CpuId(7);
        var (_, _, cx, _) = CpuId(unchecked((int)0x80000001));
        if ((d1 & (1 << 26)) != 0) f.Add("SSE2");
        if ((c1 & (1 << 20)) != 0) f.Add("SSE4.2");
        if ((c1 & (1 << 28)) != 0) f.Add("AVX");
        if ((c1 & (1 << 12)) != 0) f.Add("FMA3");
        if ((b7 & (1 << 5)) != 0) f.Add("AVX2");
        if ((b7 & (1 << 16)) != 0) f.Add("AVX-512F");
        if ((c1 & (1 << 5)) != 0) f.Add("VT-x");
        if ((cx & (1 << 2)) != 0) f.Add("AMD-V");
        if ((c1 & (1 << 31)) != 0) f.Add("Hypervisor");
        if ((d7 & (1 << 15)) != 0) f.Add("Hybrid");
        return f;
    }

    // ---- Processor topology ------------------------------------------------------------------
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    public static CoreTopology? GetTopology()
    {
        if (!OperatingSystem.IsWindows()) return null;
        uint len = 0;
        GetLogicalProcessorInformationEx(0xFFFF, IntPtr.Zero, ref len);
        if (len == 0) return null;
        var buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(0xFFFF, buf, ref len)) return null;
            int cores = 0, logical = 0;
            var classes = new List<byte>();
            var l3 = new List<long>();
            long l2 = 0;
            var offset = 0;
            while (offset < len)
            {
                var ptr = buf + offset;
                var rel = Marshal.ReadInt32(ptr);
                var size = Marshal.ReadInt32(ptr, 4);
                if (size <= 0) break;
                switch (rel)
                {
                    case 0: // RelationProcessorCore
                        cores++;
                        var flags = Marshal.ReadByte(ptr, 8);
                        classes.Add(Marshal.ReadByte(ptr, 9));
                        var groupCount = Marshal.ReadInt16(ptr, 30);
                        for (var g = 0; g < groupCount; g++)
                        {
                            // GROUP_AFFINITY { KAFFINITY Mask; WORD Group; WORD Reserved[3]; } starts at offset 32
                            var mask = (ulong)Marshal.ReadInt64(ptr, 32 + g * 16);
                            logical += System.Numerics.BitOperations.PopCount(mask);
                        }
                        _ = flags;
                        break;
                    case 2: // RelationCache
                        var level = Marshal.ReadByte(ptr, 8);
                        var cacheSize = (uint)Marshal.ReadInt32(ptr, 12);
                        var type = Marshal.ReadInt32(ptr, 16);
                        if (level == 3) l3.Add(cacheSize);
                        if (level == 2 && type is 0 or 2) l2 += cacheSize;
                        break;
                }
                offset += size;
            }
            var distinct = classes.Distinct().OrderBy(c => c).ToList();
            var hybrid = distinct.Count > 1;
            var max = distinct.Count > 0 ? distinct[^1] : (byte)0;
            var p = hybrid ? classes.Count(c => c == max) : cores;
            return new CoreTopology(cores, logical, p, cores - p, hybrid, l3, l2);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ---- Firmware / power / TPM ------------------------------------------------------------
    [DllImport("kernel32.dll")] private static extern bool GetFirmwareType(out int type);

    /// <summary>1 = legacy BIOS, 2 = UEFI.</summary>
    public static int? FirmwareType() => OperatingSystem.IsWindows() && GetFirmwareType(out var t) ? t : null;

    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inLen, IntPtr output, uint outLen);

    /// <summary>(Modern Standby S0 low power idle, batteries present).</summary>
    public static (bool aoac, bool batteries)? PowerCapabilities()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var buf = Marshal.AllocHGlobal(128);
        try
        {
            if (CallNtPowerInformation(4, IntPtr.Zero, 0, buf, 128) != 0) return null; // SystemPowerCapabilities
            return (Marshal.ReadByte(buf, 20) != 0, Marshal.ReadByte(buf, 30) != 0);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TPM_DEVICE_INFO { public uint structVersion, tpmVersion, tpmInterfaceType, tpmImpRevision; }

    [DllImport("tbs.dll")] private static extern uint Tbsi_GetDeviceInfo(uint size, out TPM_DEVICE_INFO info);

    /// <summary>TPM version without admin rights: "2.0", "1.2", or null when absent.</summary>
    public static (bool present, string? version) Tpm()
    {
        if (!OperatingSystem.IsWindows()) return (false, null);
        try
        {
            var rc = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TPM_DEVICE_INFO>(), out var info);
            if (rc != 0) return (false, null);
            return (true, info.tpmVersion switch { 1 => "1.2", 2 => "2.0", _ => info.tpmVersion.ToString() });
        }
        catch (DllNotFoundException) { return (false, null); }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public static MEMORYSTATUSEX? MemoryStatus()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m : null;
    }
}
