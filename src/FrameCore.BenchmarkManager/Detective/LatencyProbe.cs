using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FrameCore.Core;
using FrameCore.Core.Localization;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>
/// Internal latency, the way LatencyMon measures it: every deferred procedure call (DPC) and interrupt routine (ISR)
/// is timed by Windows' kernel trace and attributed to the driver that owns the routine. Long DPC/ISR = audio
/// crackles, input lag and frame-time spikes. Requires administrator rights (runs in the elevated worker).
/// </summary>
public static class LatencyProbe
{
    public sealed record Sample(string Driver, bool Isr, double Ms);

    public static LatencyReport Capture(int seconds, CancellationToken ct = default)
    {
        if (!AppInfo.IsElevated) return new LatencyReport { Error = Loc.T("detective.lat.needsAdmin") };
        var drivers = DriverMap.Load();
        var samples = new List<Sample>(1 << 16);
        var sw = Stopwatch.StartNew();
        try
        {
            // The NT Kernel Logger is unique: if another tool already uses it, we report it instead of stopping it.
            using var session = new TraceEventSession(KernelTraceEventParser.KernelSessionName) { StopOnDispose = true };
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.DeferedProcedureCalls | KernelTraceEventParser.Keywords.Interrupt);
            void Add(ulong routine, bool isr, double ms) { lock (samples) if (samples.Count < 2_000_000) samples.Add(new Sample(drivers.Find(routine), isr, ms)); }
            session.Source.Kernel.PerfInfoDPC += d => Add(d.Routine, false, d.ElapsedTimeMSec);
            session.Source.Kernel.PerfInfoTimerDPC += d => Add(d.Routine, false, d.ElapsedTimeMSec);
            session.Source.Kernel.PerfInfoThreadedDPC += d => Add(d.Routine, false, d.ElapsedTimeMSec);
            session.Source.Kernel.PerfInfoISR += d => Add(d.Routine, true, d.ElapsedTimeMSec);
            var pump = Task.Run(() => session.Source.Process());
            while (sw.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested) Thread.Sleep(200);
            session.Stop();
            pump.Wait(5000);
        }
        catch (Exception ex)
        {
            Log.Error("Latency", "Kernel trace failed", ex);
            return new LatencyReport { Error = Loc.T("detective.lat.failed", ex.Message) };
        }
        List<Sample> copy;
        lock (samples) copy = samples.ToList();
        return Analyze(copy, sw.Elapsed.TotalSeconds);
    }

    /// <summary>Per-driver summary (pure, tested).</summary>
    public static LatencyReport Analyze(IReadOnlyList<Sample> samples, double seconds)
    {
        var r = new LatencyReport { Seconds = seconds };
        if (seconds <= 0) return r;
        // Keep only plausible durations: a few bogus negative/huge values appear when a trace buffer is lost.
        var ok = samples.Where(s => s.Ms >= 0 && s.Ms < 1000).ToList();
        r.DpcCount = ok.LongCount(s => !s.Isr);
        r.IsrCount = ok.LongCount(s => s.Isr);
        r.MaxDpcMs = ok.Where(s => !s.Isr).Select(s => s.Ms).DefaultIfEmpty().Max();
        r.MaxIsrMs = ok.Where(s => s.Isr).Select(s => s.Ms).DefaultIfEmpty().Max();
        r.BusyPercent = ok.Sum(s => s.Ms) / (seconds * 1000) * 100;
        foreach (var g in ok.GroupBy(s => s.Driver, StringComparer.OrdinalIgnoreCase))
        {
            var d = g.Select(x => x.Ms).OrderBy(x => x).ToList();
            r.Drivers.Add(new DriverLatency
            {
                Driver = g.Key, Component = ComponentOf(g.Key), Count = d.Count, MaxMs = d[^1],
                P99Ms = d[Math.Clamp((int)Math.Ceiling(d.Count * 0.99) - 1, 0, d.Count - 1)], TotalMs = d.Sum(),
            });
        }
        r.Drivers = r.Drivers.OrderByDescending(d => d.MaxMs).ThenByDescending(d => d.TotalMs).ToList();
        return r;
    }

    /// <summary>Verdict thresholds used by LatencyMon-style tools: above ~1 ms a DPC/ISR can cause audible/visible hitches.</summary>
    public static Core.Models.Severity Verdict(double maxMs) =>
        maxMs >= 2 ? Core.Models.Severity.Critical : maxMs >= 1 ? Core.Models.Severity.Warning : maxMs >= 0.5 ? Core.Models.Severity.Suggestion : Core.Models.Severity.Info;

    private static readonly (string Pattern, string Component)[] Components =
    {
        ("ndis|tcpip|netio|netwtw|netwlv|rt640|rt68|rtwlane|rtux|e1d|e2f|ixgbe|athw|bcmwl|mrvlpcie|killer|wdiwifi|vwifi|nwifi|mtkwl|rtl8", "network"),
        ("nvlddmkm|amdkmdag|atikmdag|igdkmd|dxgkrnl|dxgmms|nvvad|amdacpbus|amdfendr|basicdisplay", "graphics"),
        ("storport|stornvme|storahci|iastor|nvme|ataport|disk|classpnp|volmgr|partmgr|amdsata|amdraid|rcraid|secnvme|samsung", "storage"),
        ("hdaudbus|hdaudio|portcls|rtkvhd|rthdasio|ks\\.sys|ksthunk|usbaudio|nvhda|atihdw|intcaudiobus|intcdaud|nahimic|sonic", "audio"),
        ("usbxhci|usbport|usbhub|ucx01000|ehci|usbccgp|hidusb|hidclass|mouhid|kbdhid|i8042prt|winusb", "usb"),
        ("acpi|intelppm|amdppm|processr|pci\\.sys|pcw|battc|cmbatt|intelpep|amdpsp|amdi2c|iastora|msisadrv", "power"),
        ("ntoskrnl|hal\\.dll|wdf01000|wdfldr|ci\\.dll|fltmgr|tm\\.sys|clipsp|cng|ksecdd|wdfilter|wdnisdrv|mpkslx", "kernel"),
    };

    public static string ComponentOf(string driver)
    {
        var d = driver.ToLowerInvariant();
        foreach (var (p, c) in Components)
            if (System.Text.RegularExpressions.Regex.IsMatch(d, p)) return c;
        return "other";
    }
}

/// <summary>Kernel modules loaded now (base addresses), to attribute a routine address to its driver.</summary>
public sealed class DriverMap
{
    private readonly ulong[] _bases;
    private readonly string[] _names;
    private DriverMap(ulong[] bases, string[] names) { _bases = bases; _names = names; }

    public static DriverMap FromModules(IEnumerable<(ulong Base, string Name)> modules)
    {
        var list = modules.Where(m => m.Base != 0).OrderBy(m => m.Base).ToList();
        return new DriverMap(list.Select(m => m.Base).ToArray(), list.Select(m => m.Name).ToArray());
    }

    /// <summary>Driver owning the address: the module with the highest base not above it.</summary>
    public string Find(ulong address)
    {
        if (_bases.Length == 0 || address < _bases[0]) return "?";
        var i = Array.BinarySearch(_bases, address);
        if (i < 0) i = ~i - 1;
        return _names[Math.Clamp(i, 0, _names.Length - 1)];
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumDeviceDrivers([Out] ulong[]? lpImageBase, uint cb, out uint lpcbNeeded);
    [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDeviceDriverBaseNameW(ulong imageBase, StringBuilder lpBaseName, uint nSize);

    /// <summary>Real addresses are only returned to elevated processes; otherwise the map is empty ("?").</summary>
    public static DriverMap Load()
    {
        if (!OperatingSystem.IsWindows()) return FromModules(Array.Empty<(ulong, string)>());
        try
        {
            EnumDeviceDrivers(null, 0, out var needed);
            var bases = new ulong[needed / 8 + 16];
            if (!EnumDeviceDrivers(bases, (uint)(bases.Length * 8), out needed)) return FromModules(Array.Empty<(ulong, string)>());
            var count = (int)(needed / 8);
            var list = new List<(ulong, string)>(count);
            var sb = new StringBuilder(260);
            for (var i = 0; i < count; i++)
            {
                sb.Clear();
                if (GetDeviceDriverBaseNameW(bases[i], sb, (uint)sb.Capacity) > 0) list.Add((bases[i], sb.ToString()));
            }
            return FromModules(list);
        }
        catch (Exception ex) { Log.Warn("Latency", "Driver list unavailable: " + ex.Message); return FromModules(Array.Empty<(ulong, string)>()); }
    }
}
