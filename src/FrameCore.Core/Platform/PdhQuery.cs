using System.Runtime.InteropServices;

namespace FrameCore.Core.Platform;

/// <summary>
/// Performance Data Helper with English counter paths (PdhAddEnglishCounterW), so it works on French,
/// German… Windows alike. Rates (% counters) need two collections; the first sample returns nothing.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.OrdinalIgnoreCase);
    private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    public PdhQuery()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) _query = IntPtr.Zero;
    }

    public bool IsValid => _query != IntPtr.Zero;

    /// <summary>Adds a counter; returns false if the counter does not exist on this system.</summary>
    public bool Add(string key, string englishPath)
    {
        if (_query == IntPtr.Zero) return false;
        if (PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var c) != 0) return false;
        _counters[key] = c;
        return true;
    }

    public bool Has(string key) => _counters.ContainsKey(key);

    public void Collect()
    {
        if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
    }

    public double? Get(string key)
    {
        if (!_counters.TryGetValue(key, out var c)) return null;
        if (PdhGetFormattedCounterValue(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) != 0) return null;
        return v.CStatus is 0 or 1 ? v.DoubleValue : null; // PDH_CSTATUS_VALID_DATA / NEW_DATA
    }

    /// <summary>Wildcard counters (e.g. "\GPU Engine(*)\Utilization Percentage") → instance name → value.</summary>
    public Dictionary<string, double> GetArray(string key)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!_counters.TryGetValue(key, out var c)) return result;
        uint size = 0;
        var rc = PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, IntPtr.Zero);
        if (rc != PDH_MORE_DATA || size == 0) return result;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return result;
            var itemSize = IntPtr.Size + 16;
            for (var i = 0; i < count; i++)
            {
                var item = buf + i * itemSize;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                var status = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8));
                if (status is 0 or 1) result[name] = result.TryGetValue(name, out var existing) ? existing + value : value;
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
