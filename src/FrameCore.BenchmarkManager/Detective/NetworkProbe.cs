using System.Net.NetworkInformation;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>
/// Where network latency comes from: the PC and its link (ping to the router), or beyond (ping to the Internet),
/// at rest and while the connection is loaded (bufferbloat). Also checks the link itself (100 Mb/s cable, Wi-Fi,
/// VPN/virtual adapters, traffic in the background).
/// </summary>
public static class NetworkProbe
{
    public static async Task<PingSummary> PingAsync(string target, int count, int intervalMs, CancellationToken ct)
    {
        var s = new PingSummary { Target = target };
        using var ping = new Ping();
        for (var i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            s.Sent++;
            try
            {
                var r = await ping.SendPingAsync(target, 1000);
                if (r.Status == IPStatus.Success) { s.Received++; s.Samples.Add(r.RoundtripTime); }
            }
            catch { }
            try { await Task.Delay(intervalMs, ct); } catch (OperationCanceledException) { break; }
        }
        Summarize(s);
        return s;
    }

    public static void Summarize(PingSummary s)
    {
        if (s.Samples.Count == 0) return;
        var x = s.Samples;
        s.AvgMs = x.Average();
        s.MaxMs = x.Max();
        s.P95Ms = x.OrderBy(v => v).ElementAt(Math.Clamp((int)Math.Ceiling(x.Count * 0.95) - 1, 0, x.Count - 1));
        s.JitterMs = x.Count < 2 ? 0 : x.Zip(x.Skip(1), (a, b) => Math.Abs(b - a)).Average();
    }

    /// <summary>Bytes per second received + sent by the active adapters over the window (traffic in the background).</summary>
    public static double BackgroundKBps(TimeSpan window)
    {
        static long Total() => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Sum(n => { try { var s = n.GetIPStatistics(); return s.BytesReceived + s.BytesSent; } catch { return 0L; } });
        var a = Total();
        Thread.Sleep(window);
        return Math.Max(0, Total() - a) / window.TotalSeconds / 1024;
    }

    private static readonly string[] VirtualMarkers = { "tap-windows", "wireguard", "wintun", "nordlynx", "hamachi", "zerotier", "radmin", "openvpn", "proton", "expressvpn", "surfshark", "cloudflare warp", "tailscale", "fortinet", "cisco anyconnect", "globalprotect", "pangp", "juniper" };

    /// <summary>Findings about the link and the measured latency (pure, tested).</summary>
    public static List<DetectiveFinding> Analyze(NetworkReport n, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var f = new List<DetectiveFinding>();
        var up = adapters.Where(a => a.IsUp && a.IsPhysical).ToList();
        var active = up.FirstOrDefault(a => a.Gateway != null) ?? up.FirstOrDefault();
        if (active == null)
        {
            f.Add(new DetectiveFinding { Id = "net.none", Area = DetectiveArea.Network, Severity = Severity.Info, Title = Loc.T("detective.net.none") });
            return f;
        }
        if (!active.IsWireless && active.SpeedMbps is > 0 and <= 100)
            f.Add(new DetectiveFinding
            {
                Id = "net.link100", Area = DetectiveArea.Network, Severity = Severity.Warning, Title = Loc.T("detective.net.link100.title"),
                Evidence = Loc.T("detective.net.link100.evidence", active.Description, active.SpeedMbps), Advice = Loc.T("detective.net.link100.advice"),
            });
        if (active.IsWireless)
        {
            var wiredAvailable = adapters.Any(a => a.IsPhysical && !a.IsWireless && a.Type.Contains("Ethernet", StringComparison.OrdinalIgnoreCase));
            f.Add(new DetectiveFinding
            {
                Id = "net.wifi", Area = DetectiveArea.Network, Severity = Severity.Suggestion, Title = Loc.T("detective.net.wifi.title"),
                Evidence = Loc.T("detective.net.wifi.evidence", active.Description, n.Gateway?.JitterMs.ToString("0.0") ?? "?"),
                Advice = Loc.T(wiredAvailable ? "detective.net.wifi.cable" : "detective.net.wifi.advice"),
            });
        }
        else if (up.Any(a => a.IsWireless))
            f.Add(new DetectiveFinding
            {
                Id = "net.wifiAlsoOn", Area = DetectiveArea.Network, Severity = Severity.Suggestion, Title = Loc.T("detective.net.wifiAlsoOn.title"),
                Evidence = string.Join(", ", up.Where(a => a.IsWireless).Select(a => a.Description)), Advice = Loc.T("detective.net.wifiAlsoOn.advice"),
            });
        var virt = adapters.Where(a => a.IsUp && VirtualMarkers.Any(m => (a.Description + " " + a.Name).Contains(m, StringComparison.OrdinalIgnoreCase))).Select(a => a.Description).ToList();
        n.VirtualAdapters = virt;
        if (virt.Count > 0)
            f.Add(new DetectiveFinding
            {
                Id = "net.vpn", Area = DetectiveArea.Network, Severity = Severity.Suggestion, Title = Loc.T("detective.net.vpn.title"),
                Evidence = string.Join(", ", virt), Advice = Loc.T("detective.net.vpn.advice"),
            });
        if (n.Gateway is { Received: > 0 } g)
        {
            var bad = g.LossPercent > 0 || g.P95Ms > (active.IsWireless ? 15 : 3) || g.JitterMs > (active.IsWireless ? 5 : 1.5);
            f.Add(new DetectiveFinding
            {
                Id = "net.gateway", Area = DetectiveArea.Network, Severity = bad ? (g.LossPercent >= 2 || g.P95Ms > 30 ? Severity.Warning : Severity.Suggestion) : Severity.Info,
                Title = Loc.T(bad ? "detective.net.gateway.bad" : "detective.net.gateway.ok"),
                Evidence = Loc.T("detective.net.ping.evidence", g.Target, g.AvgMs.ToString("0.0"), g.P95Ms.ToString("0"), g.JitterMs.ToString("0.0"), g.LossPercent.ToString("0.#")),
                Advice = bad ? Loc.T(active.IsWireless ? "detective.net.gateway.adviceWifi" : "detective.net.gateway.adviceWired") : "",
            });
        }
        if (n.Internet is { Received: > 0 } i)
        {
            var extra = i.AvgMs - (n.Gateway?.AvgMs ?? 0);
            var bad = i.LossPercent >= 1 || i.JitterMs > 8;
            f.Add(new DetectiveFinding
            {
                Id = "net.internet", Area = DetectiveArea.Network, Severity = bad ? Severity.Suggestion : Severity.Info,
                Title = Loc.T(bad ? "detective.net.internet.bad" : "detective.net.internet.ok", extra.ToString("0")),
                Evidence = Loc.T("detective.net.ping.evidence", i.Target, i.AvgMs.ToString("0.0"), i.P95Ms.ToString("0"), i.JitterMs.ToString("0.0"), i.LossPercent.ToString("0.#")),
                Advice = bad ? Loc.T("detective.net.internet.advice") : "",
            });
        }
        if (n.InternetLoaded is { Received: > 0 } li && n.Internet is { Received: > 0 } idle)
        {
            var added = li.P95Ms - idle.AvgMs;
            var grade = added < 30 ? "A" : added < 60 ? "B" : added < 200 ? "C" : "D";
            f.Add(new DetectiveFinding
            {
                Id = "net.bufferbloat", Area = DetectiveArea.Network, Severity = grade is "A" ? Severity.Info : grade is "B" ? Severity.Suggestion : Severity.Warning,
                Title = Loc.T("detective.net.bloat.title", grade), Evidence = Loc.T("detective.net.bloat.evidence", added.ToString("0")),
                Advice = grade is "A" ? "" : Loc.T("detective.net.bloat.advice"),
            });
        }
        if (n.BackgroundKBps > 200)
            f.Add(new DetectiveFinding
            {
                Id = "net.background", Area = DetectiveArea.Network, Severity = n.BackgroundKBps > 2000 ? Severity.Warning : Severity.Suggestion,
                Title = Loc.T("detective.net.background.title"), Evidence = Loc.T("detective.net.background.evidence", n.BackgroundKBps.ToString("0")),
                Advice = Loc.T("detective.net.background.advice"),
            });
        foreach (var (key, label) in new[] { ("*EEE", "EEE"), ("*FlowControl", "FlowControl"), ("*InterruptModeration", "InterruptModeration") })
            if (active.AdvancedProperties.TryGetValue(key, out var v) && key == "*EEE" && v is "1" or "Enabled")
                f.Add(new DetectiveFinding
                {
                    Id = "net.eee", Area = DetectiveArea.Network, Severity = Severity.Suggestion, Title = Loc.T("detective.net.eee.title"),
                    Evidence = active.Description, Advice = Loc.T("detective.net.eee.advice"), Fix = DetectiveFix.OpenPage, FixTarget = "pro:Network",
                });
        return f;
    }
}

/// <summary>Frame-time spikes: how often, and why they probably happen.</summary>
public sealed class StutterAnalysis
{
    public int Frames { get; set; }
    public int Spikes { get; set; }
    public double SpikesPerMinute { get; set; }
    public double MedianMs { get; set; }
    /// <summary>periodic | startup | random | none</summary>
    public string Pattern { get; set; } = "none";
    public double? PeriodSeconds { get; set; }
    /// <summary>Frame rate reached 95 % of the time: a cap near it gives steadier frames.</summary>
    public double SteadyFps { get; set; }
    public double? VrrCapFps { get; set; }
}

public static class StutterAnalyzer
{
    /// <summary>
    /// A spike is a frame longer than twice the median and at least 8 ms longer. Spikes regularly spaced point to a
    /// background task; spikes concentrated at the start point to shader compilation / loading; others are random
    /// (driver latency, CPU limit, memory).
    /// </summary>
    public static StutterAnalysis Analyze(IReadOnlyList<double> frameTimesMs, double? refreshHz = null)
    {
        var a = new StutterAnalysis { Frames = frameTimesMs.Count };
        if (frameTimesMs.Count < 60) return a;
        var sorted = frameTimesMs.OrderBy(x => x).ToList();
        var median = sorted[sorted.Count / 2];
        a.MedianMs = median;
        var p95 = sorted[Math.Clamp((int)(sorted.Count * 0.95), 0, sorted.Count - 1)];
        a.SteadyFps = p95 > 0 ? Math.Floor(1000 / p95) : 0;
        if (refreshHz is > 30 and var hz) a.VrrCapFps = Math.Floor(3600 * hz / (3600 + hz)); // the cap used by NVIDIA Reflex with G-SYNC
        var t = 0.0;
        var times = new List<double>();
        foreach (var ft in frameTimesMs)
        {
            t += ft;
            if (ft > 2 * median && ft - median >= 8) times.Add(t / 1000);
        }
        var total = t / 1000;
        a.Spikes = times.Count;
        a.SpikesPerMinute = total > 0 ? times.Count / total * 60 : 0;
        if (times.Count < 3) { a.Pattern = times.Count == 0 ? "none" : "random"; return a; }
        if (times.Count(x => x < total * 0.2) >= times.Count * 0.6) { a.Pattern = "startup"; return a; }
        var gaps = times.Zip(times.Skip(1), (x, y) => y - x).Where(g => g > 0.25).ToList();
        if (gaps.Count >= 3)
        {
            var mean = gaps.Average();
            var cv = Math.Sqrt(gaps.Sum(g => (g - mean) * (g - mean)) / gaps.Count) / mean;
            if (cv < 0.25) { a.Pattern = "periodic"; a.PeriodSeconds = Math.Round(mean, 1); return a; }
        }
        a.Pattern = "random";
        return a;
    }
}
