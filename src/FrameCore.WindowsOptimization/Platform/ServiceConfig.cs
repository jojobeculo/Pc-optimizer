using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FrameCore.WindowsOptimization.Platform;

/// <summary>Service start type as shown to users: Automatic, AutomaticDelayed, Manual, Disabled, Boot, System.</summary>
public interface IServiceConfigurator
{
    bool Exists(string name);
    string GetStartType(string name);
    void SetStartType(string name, string startType);
}

public sealed class WindowsServiceConfigurator : IServiceConfigurator
{
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_CONFIG = 0x0001, SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    public bool Exists(string name)
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var svc = OpenServiceW(scm, name, SERVICE_QUERY_CONFIG);
            if (svc == IntPtr.Zero) return false;
            CloseServiceHandle(svc);
            return true;
        }
        finally { CloseServiceHandle(scm); }
    }

    public string GetStartType(string name)
    {
        using var h = Open(name, SERVICE_QUERY_CONFIG);
        QueryServiceConfigW(h.Service, IntPtr.Zero, 0, out var needed);
        var buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfigW(h.Service, buf, needed, out _)) throw new Win32Exception();
            var startType = (uint)Marshal.ReadInt32(buf, 4); // QUERY_SERVICE_CONFIG: dwServiceType, dwStartType, ...
            if (startType == 2 && IsDelayed(h.Service)) return "AutomaticDelayed";
            return startType switch { 0 => "Boot", 1 => "System", 2 => "Automatic", 3 => "Manual", 4 => "Disabled", _ => startType.ToString() };
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static bool IsDelayed(IntPtr svc)
    {
        var buf = Marshal.AllocHGlobal(16);
        try { return QueryServiceConfig2W(svc, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, buf, 16, out _) && Marshal.ReadInt32(buf) != 0; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void SetStartType(string name, string startType)
    {
        uint native = startType switch
        {
            "Automatic" or "AutomaticDelayed" => 2,
            "Manual" => 3,
            "Disabled" => 4,
            _ => throw new ArgumentException($"Start type not settable: {startType}"),
        };
        using var h = Open(name, SERVICE_QUERY_CONFIG | SERVICE_CHANGE_CONFIG);
        if (!ChangeServiceConfigW(h.Service, SERVICE_NO_CHANGE, native, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
            throw new Win32Exception();
        if (native == 2)
        {
            var info = new SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = startType == "AutomaticDelayed" };
            if (!ChangeServiceConfig2W(h.Service, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info)) throw new Win32Exception();
        }
    }

    private sealed class Handles : IDisposable
    {
        public IntPtr Scm, Service;
        public void Dispose()
        {
            if (Service != IntPtr.Zero) CloseServiceHandle(Service);
            if (Scm != IntPtr.Zero) CloseServiceHandle(Scm);
        }
    }

    private static Handles Open(string name, uint access)
    {
        var h = new Handles { Scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT) };
        if (h.Scm == IntPtr.Zero) throw new Win32Exception();
        h.Service = OpenServiceW(h.Scm, name, access);
        if (h.Service == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            h.Dispose();
            if (err == 5) throw new UnauthorizedAccessException($"Access denied to service {name}");
            throw new Win32Exception(err, err == ERROR_SERVICE_DOES_NOT_EXIST ? $"Service {name} does not exist" : null);
        }
        return h;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_DELAYED_AUTO_START_INFO { [MarshalAs(UnmanagedType.Bool)] public bool fDelayedAutostart; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? db, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr h);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryServiceConfigW(IntPtr svc, IntPtr buf, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryServiceConfig2W(IntPtr svc, uint level, IntPtr buf, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfigW(IntPtr svc, uint type, uint start, uint error, string? path, string? group,
        IntPtr tag, string? deps, string? account, string? password, string? display);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig2W(IntPtr svc, uint level, ref SERVICE_DELAYED_AUTO_START_INFO info);
}

public sealed class InMemoryServiceConfigurator : IServiceConfigurator
{
    public Dictionary<string, string> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Exists(string name) => Services.ContainsKey(name);
    public string GetStartType(string name) => Services.TryGetValue(name, out var s) ? s : throw new Win32Exception(1060);
    public void SetStartType(string name, string startType) { if (!Services.ContainsKey(name)) throw new Win32Exception(1060); Services[name] = startType; }
}
