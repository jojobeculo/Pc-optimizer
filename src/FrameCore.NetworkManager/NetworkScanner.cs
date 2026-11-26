using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.NetworkManager;

public sealed class NetworkScanner : IHardwareEnricher
{
    public const string NetClass = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private static readonly string[] Keywords =
    {
        "*EEE", "EEELinkAdvertisement", "AdvancedEEE", "EnableGreenEthernet", "GreenEthernet", "*InterruptModeration", "*RSS",
        "*NumRssQueues", "*FlowControl", "*SpeedDuplex", "*JumboPacket", "*LsoV2IPv4", "*PMARPOffload", "*WakeOnMagicPacket", "PowerSavingMode",
    };
    private static readonly string[] VirtualMarkers = { "Virtual", "Hyper-V", "VPN", "TAP", "Loopback", "WAN Miniport", "VMware", "VirtualBox", "Npcap", "WireGuard", "Tailscale", "ZeroTier", "Bluetooth" };

    private readonly IRegistryAccess _reg;
    public NetworkScanner(IRegistryAccess reg) => _reg = reg;
    public string Name => Loc.T("scan.network");

    public void Enrich(MachineProfile profile) => profile.Network = Scan();

    public List<NetworkAdapterInfo> Scan()
    {
        var list = new List<NetworkAdapterInfo>();
        var classKeys = _reg.GetSubKeyNames(NetClass).Where(s => s.Length == 4 && s.All(char.IsDigit))
            .Select(s => NetClass + "\\" + s)
            .ToDictionary(p => _reg.GetValue(p, "NetCfgInstanceId")?.Data?.ToString() ?? Guid.NewGuid().ToString(), p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var isVirtual = VirtualMarkers.Any(m => ni.Description.Contains(m, StringComparison.OrdinalIgnoreCase));
            var info = new NetworkAdapterInfo
            {
                Name = ni.Name,
                Description = ni.Description,
                Type = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    _ => ni.NetworkInterfaceType.ToString(),
                },
                SpeedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0,
                IsUp = ni.OperationalStatus == OperationalStatus.Up,
                IsPhysical = !isVirtual,
                IsWireless = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                InterfaceGuid = ni.Id,
            };
            try
            {
                var props = ni.GetIPProperties();
                info.Gateway = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString();
                info.DnsServers = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
            }
            catch { }
            if (classKeys.TryGetValue(ni.Id, out var key))
            {
                info.DriverVersion = _reg.GetValue(key, "DriverVersion")?.Data?.ToString();
                foreach (var kw in Keywords)
                {
                    var hasParam = _reg.KeyExists(key + @"\Ndi\Params\" + kw);
                    var v = _reg.GetValue(key, kw)?.Data?.ToString() ?? (hasParam ? _reg.GetValue(key + @"\Ndi\Params\" + kw, "default")?.Data?.ToString() : null);
                    if (v != null) info.AdvancedProperties[kw] = v;
                }
            }
            list.Add(info);
        }
        return list.OrderByDescending(n => n.IsUp).ThenByDescending(n => n.IsPhysical).ToList();
    }
}

public sealed class PingStats
{
    public string Target { get; set; } = "";
    public int Sent { get; set; }
    public int Received { get; set; }
    public double? AvgMs { get; set; }
    public double? MinMs { get; set; }
    public double? MaxMs { get; set; }
    public double? JitterMs { get; set; }
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
}

/// <summary>User-initiated latency, jitter and packet-loss measurement (ICMP), and DNS resolution timing.</summary>
public static class LatencyTester
{
    public static async Task<PingStats> PingAsync(string target, int count = 20, int intervalMs = 200, CancellationToken ct = default)
    {
        var stats = new PingStats { Target = target };
        var samples = new List<double>();
        using var ping = new Ping();
        for (var i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            stats.Sent++;
            try
            {
                var reply = await ping.SendPingAsync(target, 1000).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) { stats.Received++; samples.Add(reply.RoundtripTime); }
            }
            catch { }
            await Task.Delay(intervalMs, ct).ConfigureAwait(false);
        }
        if (samples.Count > 0)
        {
            stats.AvgMs = samples.Average(); stats.MinMs = samples.Min(); stats.MaxMs = samples.Max();
            stats.JitterMs = samples.Count > 1 ? samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average() : 0;
        }
        return stats;
    }

    public static async Task<double?> DnsResolveMsAsync(string host)
    {
        var sw = Stopwatch.StartNew();
        try { await Dns.GetHostAddressesAsync(host).ConfigureAwait(false); return sw.Elapsed.TotalMilliseconds; }
        catch { return null; }
    }
}

/// <summary>Repair "net.autotuning.normal": TCP receive window auto-tuning back to Normal if it was disabled/restricted.</summary>
public sealed class AutotuningHandler : IActionHandler
{
    public string Name => "net.autotuning.normal";
    public bool RequiresElevation(ActionDefinition a) => true;
    private static readonly string[] Levels = { "disabled", "highlyrestricted", "restricted", "normal", "experimental" };

    public static int? Level()
    {
        var rows = Wmi.Query("SELECT SettingName, AutoTuningLevelLocal FROM MSFT_NetTCPSetting", Wmi.StandardCimv2);
        var row = rows.FirstOrDefault(r => r.Str("SettingName") == "Internet") ?? rows.FirstOrDefault();
        return row.Int("AutoTuningLevelLocal");
    }

    public ActionState Read(ActionDefinition a)
    {
        var l = Level();
        if (l == null) return ActionState.Unreadable(Loc.Unknown);
        return new ActionState { Matches = l == 3, Current = l is >= 0 and < 5 ? Levels[l.Value] : l.Value.ToString(CultureInfo.InvariantCulture), Target = "normal" };
    }

    public StateSnapshot Capture(ActionDefinition a) => Level() is { } l ? StateSnapshot.Of(l.ToString(CultureInfo.InvariantCulture)) : StateSnapshot.Absent();
    public void Apply(ActionDefinition a) => Set("normal");

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        if (s.Existed && int.TryParse(s.Value, out var l) && l is >= 0 and < 5) Set(Levels[l]);
    }

    public bool Verify(ActionDefinition a, StateSnapshot s) => !s.Existed || Level()?.ToString(CultureInfo.InvariantCulture) == s.Value;

    private static void Set(string level)
    {
        var r = SystemTools.Run(SystemTool.Netsh, new[] { "interface", "tcp", "set", "global", "autotuninglevel=" + level });
        if (!r.Success) throw new InvalidOperationException("netsh: " + r.StdOut);
    }
}
