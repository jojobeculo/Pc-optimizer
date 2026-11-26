using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FrameCore.WindowsOptimization.Platform;

public sealed record PowerSchemeInfo(Guid Guid, string Name, bool Active);

public interface IPowerAccess
{
    Guid GetActiveScheme();
    void SetActiveScheme(Guid scheme);
    IReadOnlyList<PowerSchemeInfo> ListSchemes();
    bool SchemeExists(Guid scheme);
    void DuplicateScheme(Guid source, Guid destination);
    void DeleteScheme(Guid scheme);
    uint? ReadAc(Guid scheme, Guid subgroup, Guid setting);
    uint? ReadDc(Guid scheme, Guid subgroup, Guid setting);
    void WriteAc(Guid scheme, Guid subgroup, Guid setting, uint value);
    void WriteDc(Guid scheme, Guid subgroup, Guid setting, uint value);
    /// <summary>Windows 11 "power mode" overlay. Undocumented powrprof exports; null when unavailable.</summary>
    Guid? GetOverlay();
    void SetOverlay(Guid overlay);
}

public static class PowerGuids
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47eeb4c3c3a1");
    public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    public static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    public static readonly Guid ProcThrottleMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    public static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBalanced = Guid.Empty;
    public static readonly Guid OverlayBetterPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static string FriendlyScheme(Guid g) =>
        g == Balanced ? "Balanced" : g == HighPerformance ? "High performance" : g == PowerSaver ? "Power saver" : g == UltimatePerformance ? "Ultimate Performance" : g.ToString();

    public static string FriendlyOverlay(Guid g) =>
        g == OverlayBestEfficiency ? "Best power efficiency" : g == Guid.Empty ? "Balanced" : g == OverlayBetterPerformance ? "Better performance" : g == OverlayBestPerformance ? "Best performance" : g.ToString();
}

/// <summary>powrprof.dll: documented APIs (language-independent, unlike powercfg output).</summary>
public sealed class WindowsPowerAccess : IPowerAccess
{
    private const uint ACCESS_SCHEME = 16;

    public Guid GetActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr));
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public void SetActiveScheme(Guid scheme) { Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme)); }

    public IReadOnlyList<PowerSchemeInfo> ListSchemes()
    {
        var list = new List<PowerSchemeInfo>();
        var active = GetActiveScheme();
        var buffer = Marshal.AllocHGlobal(16);
        try
        {
            for (uint i = 0; ; i++)
            {
                uint size = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buffer, ref size) != 0) break;
                var g = Marshal.PtrToStructure<Guid>(buffer);
                list.Add(new PowerSchemeInfo(g, FriendlyName(g) ?? PowerGuids.FriendlyScheme(g), g == active));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return list;
    }

    public bool SchemeExists(Guid scheme) => ListSchemes().Any(s => s.Guid == scheme);

    private static string? FriendlyName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0) return null;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0 ? Marshal.PtrToStringUni(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void DuplicateScheme(Guid source, Guid destination)
    {
        var dest = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.StructureToPtr(destination, dest, false);
            var p = dest;
            Check(PowerDuplicateScheme(IntPtr.Zero, ref source, ref p));
        }
        finally { Marshal.FreeHGlobal(dest); }
    }

    public void DeleteScheme(Guid scheme) => Check(PowerDeleteScheme(IntPtr.Zero, ref scheme));

    public uint? ReadAc(Guid scheme, Guid sub, Guid setting) =>
        PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var v) == 0 ? v : null;

    public uint? ReadDc(Guid scheme, Guid sub, Guid setting) =>
        PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var v) == 0 ? v : null;

    public void WriteAc(Guid scheme, Guid sub, Guid setting, uint value) => Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value));
    public void WriteDc(Guid scheme, Guid sub, Guid setting, uint value) => Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value));

    public Guid? GetOverlay()
    {
        try { return PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    public void SetOverlay(Guid overlay)
    {
        try { Check(PowerSetActiveOverlayScheme(overlay)); }
        catch (EntryPointNotFoundException) { throw new NotSupportedException("Power mode overlay is not available on this Windows build"); }
    }

    private static void Check(uint err)
    {
        if (err == 0) return;
        if (err == 5) throw new UnauthorizedAccessException("Access denied (power settings)");
        throw new Win32Exception((int)err);
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint flags, uint index, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, ref IntPtr destination);
    [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);
}

public sealed class InMemoryPowerAccess : IPowerAccess
{
    public Guid Active { get; set; } = PowerGuids.Balanced;
    public HashSet<Guid> Schemes { get; } = new() { PowerGuids.Balanced, PowerGuids.HighPerformance, PowerGuids.PowerSaver };
    public Dictionary<(Guid, Guid, Guid, bool), uint> Values { get; } = new();
    public Guid? Overlay { get; set; } = Guid.Empty;

    public Guid GetActiveScheme() => Active;
    public void SetActiveScheme(Guid s) { if (!Schemes.Contains(s)) throw new Win32Exception(2); Active = s; }
    public IReadOnlyList<PowerSchemeInfo> ListSchemes() => Schemes.Select(s => new PowerSchemeInfo(s, PowerGuids.FriendlyScheme(s), s == Active)).ToList();
    public bool SchemeExists(Guid s) => Schemes.Contains(s);
    public void DuplicateScheme(Guid source, Guid destination) => Schemes.Add(destination);
    public void DeleteScheme(Guid s) => Schemes.Remove(s);
    public uint? ReadAc(Guid s, Guid g, Guid k) => Values.TryGetValue((s, g, k, true), out var v) ? v : null;
    public uint? ReadDc(Guid s, Guid g, Guid k) => Values.TryGetValue((s, g, k, false), out var v) ? v : null;
    public void WriteAc(Guid s, Guid g, Guid k, uint v) => Values[(s, g, k, true)] = v;
    public void WriteDc(Guid s, Guid g, Guid k, uint v) => Values[(s, g, k, false)] = v;
    public Guid? GetOverlay() => Overlay;
    public void SetOverlay(Guid o) => Overlay = o;
}
