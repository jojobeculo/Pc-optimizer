using FrameCore.BenchmarkManager.Detective;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;
using Xunit;

namespace FrameCore.Tests;

public class DetectiveTests
{
    private static readonly ProcessClassifier Classifier = ProcessClassifier.Load(Path.Combine(Repo.Catalog, "knowledge"));
    public DetectiveTests() => Repo.LoadLoc();

    [Fact]
    public void Driver_latency_is_attributed_and_ranked_by_the_longest_call()
    {
        var map = DriverMap.FromModules(new (ulong, string)[] { (0x1000, "ntoskrnl.exe"), (0x5000, "ndis.sys"), (0x9000, "nvlddmkm.sys") });
        Assert.Equal("ndis.sys", map.Find(0x5abc));
        Assert.Equal("nvlddmkm.sys", map.Find(0xFFFF));
        Assert.Equal("?", map.Find(0x10));
        var samples = new List<LatencyProbe.Sample>();
        for (var i = 0; i < 1000; i++) samples.Add(new("ndis.sys", false, 0.02));
        samples.Add(new("ndis.sys", false, 2.4));
        for (var i = 0; i < 500; i++) samples.Add(new("nvlddmkm.sys", true, 0.05));
        samples.Add(new("bogus.sys", false, -3));
        var r = LatencyProbe.Analyze(samples, 10);
        Assert.Equal("ndis.sys", r.Drivers[0].Driver);
        Assert.Equal("network", r.Drivers[0].Component);
        Assert.Equal(2.4, r.MaxDpcMs, 3);
        Assert.Equal(500, r.IsrCount);
        Assert.Equal(Severity.Critical, LatencyProbe.Verdict(2.4));
        var findings = DetectiveAnalysis.Latency(r);
        Assert.Contains(findings, f => f.Id == "lat.ndis.sys" && f.Severity == Severity.Critical);
    }

    [Fact]
    public void Wakeups_and_cpu_are_computed_from_two_snapshots()
    {
        var a = new[] { new ProcessCounters(10, "app", 3, 0, 1000, 100 << 20, 1), new ProcessCounters(20, "old", 1, 0, 5, 0, 1) };
        var b = new[] { new ProcessCounters(10, "app", 3, 2_000_000, 3000, 100 << 20, 1), new ProcessCounters(20, "new", 1, 0, 50, 0, 1) };
        var d = ProcessSampler.Delta(a, b, 2, 4);
        var app = d.Single(x => x.Pid == 10);
        Assert.Equal(1000, app.WakeupsPerSec, 3);        // 2000 switches over 2 s
        Assert.Equal(2.5, app.CpuPercent, 3);            // 0.2 s CPU over 2 s on 4 CPUs
        Assert.Equal(25, d.Single(x => x.Pid == 20).WakeupsPerSec, 3); // PID reused by another program: counted from zero
    }

    private static ProcessVerdict C(string name, string? path = null, bool window = false, string? company = "Vendor", params string[] services) =>
        Classifier.Classify(new ProcessFacts { Pid = 1234, Name = name, Path = path, HasWindow = window, Company = company, Services = services.ToList() }, null, new ClassificationContext());

    [Fact]
    public void Known_processes_get_their_tier_and_protected_ones_are_never_acted_on()
    {
        Assert.True(Classifier.RuleCount > 40);
        Assert.Equal(ProcessTier.Essential, C("MsMpEng.exe").Tier);
        Assert.True(C("vgc.exe").Protected);
        Assert.Equal(ProcessTier.Harmful, C("DriverBooster.exe").Tier);
        Assert.Equal(ProcessTier.LowValue, C("ArmouryCrate.Service.exe").Tier);
        Assert.Equal(ProcessTier.Useless, C("AdobeARM.exe").Tier);
        Assert.Equal(ProcessTier.VeryUseful, C("FortniteClient-Win64-Shipping.exe").Tier);
        Assert.Equal(ProcessTier.Essential, C("audiodg").Tier);
        Assert.True(C("lghub_agent.exe").Protected);  // peripheral software keeps DPI and macros
        foreach (var level in Enum.GetValues<ProfileLevel>())
        {
            Assert.Equal(ProcessAction.None, ProcessClassifier.ActionFor(C("vgc.exe"), level));
            Assert.Equal(ProcessAction.None, ProcessClassifier.ActionFor(C("audiodg"), level));
            Assert.Equal(ProcessAction.None, ProcessClassifier.ActionFor(C("cs2.exe"), level));
        }
    }

    [Fact]
    public void Unknown_processes_are_classified_from_what_can_be_observed()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(win)) Assert.Equal(ProcessTier.Useful, C("someservicehost", Path.Combine(win, "System32", "x.exe")).Tier);
        Assert.Equal(ProcessTier.Harmful, C("xq7z", @"C:\Users\a\AppData\Local\Temp\xq7z.exe", company: null).Tier);
        Assert.Equal(ProcessTier.Useful, C("myeditor", @"C:\Tools\myeditor.exe", window: true).Tier);
        Assert.Equal(ProcessTier.LowValue, C("vendorhelper", @"C:\Program Files\Vendor\vendorhelper.exe").Tier);
        Assert.Equal(ProcessTier.Useless, C("svchostlike", @"C:\Program Files\X\a.exe", services: new[] { "DiagTrack" }).Tier);
        Assert.Equal(ProcessTier.Essential, C("svchost", @"C:\Program Files\X\b.exe", services: new[] { "Dnscache", "DiagTrack" }).Tier);
    }

    [Fact]
    public void Deeper_levels_reach_further_down_the_tiers()
    {
        var useless = C("AdobeARM.exe"); var low = C("vendorhelper", @"C:\Program Files\Vendor\vendorhelper.exe");
        Assert.Equal(ProcessAction.None, ProcessClassifier.ActionFor(useless, ProfileLevel.Balanced));
        Assert.Equal(ProcessAction.Close, ProcessClassifier.ActionFor(useless, ProfileLevel.Performance));
        Assert.Equal(ProcessAction.None, ProcessClassifier.ActionFor(low, ProfileLevel.Performance));
        Assert.Equal(ProcessAction.LowerPriority, ProcessClassifier.ActionFor(low, ProfileLevel.Maximum));
        Assert.Equal(ProcessAction.Close, ProcessClassifier.ActionFor(low, ProfileLevel.Extreme));
        var openWindow = C("vendorhelper", @"C:\Program Files\Vendor\vendorhelper.exe", window: true);
        Assert.NotEqual(ProcessAction.Close, ProcessClassifier.ActionFor(openWindow, ProfileLevel.Extreme)); // never closes what you have open
    }

    [Fact]
    public void Crashes_are_turned_into_probable_causes()
    {
        Assert.Equal("0x124", StabilityScanner.BugcheckCode("0x00000124 (0x0000000000000000, 0x1)"));
        Assert.Equal("cpuOc", StabilityScanner.CauseKey("0x101"));
        var now = DateTime.Now;
        var events = new List<StabilityEvent>
        {
            new() { Time = now, Kind = "bugcheck", Code = "0x1A" }, new() { Time = now, Kind = "bugcheck", Code = "0x1A" },
            new() { Time = now, Kind = "unexpectedShutdown" }, new() { Time = now, Kind = "unexpectedShutdown" }, new() { Time = now, Kind = "unexpectedShutdown" },
            new() { Time = now, Kind = "whea", Code = "19" },
        };
        var f = StabilityScanner.Analyze(events, now);
        var bsod = f.Single(x => x.Id == "stab.bsod");
        Assert.Equal(Severity.Critical, bsod.Severity);
        Assert.Equal(DetectiveFix.OpenMemoryTest, bsod.Fix);
        Assert.Contains(f, x => x.Id == "stab.shutdown");
        Assert.Contains(f, x => x.Id == "stab.whea");
        Assert.Single(StabilityScanner.Analyze(new List<StabilityEvent>(), now), x => x.Id == "stab.ok");
    }

    [Fact]
    public void Network_findings_point_to_the_link_and_grade_bufferbloat()
    {
        var eth = new NetworkAdapterInfo { Description = "Realtek PCIe 2.5GbE", Type = "Ethernet", IsUp = true, IsPhysical = true, SpeedMbps = 100, Gateway = "192.168.1.1" };
        var wifi = new NetworkAdapterInfo { Description = "Intel Wi-Fi 6", Type = "Wireless80211", IsUp = true, IsPhysical = true, IsWireless = true };
        var n = new NetworkReport
        {
            Gateway = Ping("gw", 1, 1.2, 1.5), Internet = Ping("1.1.1.1", 12, 13, 14),
            InternetLoaded = Ping("1.1.1.1", 90, 140, 180), BackgroundKBps = 3000,
        };
        var f = NetworkProbe.Analyze(n, new[] { eth, wifi });
        Assert.Contains(f, x => x.Id == "net.link100");
        Assert.Contains(f, x => x.Id == "net.wifiAlsoOn");
        Assert.Contains(f, x => x.Id == "net.bufferbloat" && x.Title.Contains("C"));
        Assert.Contains(f, x => x.Id == "net.background" && x.Severity == Severity.Warning);
    }

    private static PingSummary Ping(string target, params double[] samples)
    {
        var s = new PingSummary { Target = target, Sent = samples.Length, Received = samples.Length, Samples = samples.ToList() };
        NetworkProbe.Summarize(s);
        return s;
    }

    [Fact]
    public void Stutter_patterns_are_recognised_and_a_frame_cap_is_suggested()
    {
        var periodic = new List<double>();
        for (var i = 0; i < 3000; i++) periodic.Add(i % 300 == 150 ? 40 : 6.9); // a 40 ms hitch every ~2 s at 145 FPS
        var a = StutterAnalyzer.Analyze(periodic, 360);
        Assert.Equal("periodic", a.Pattern);
        Assert.InRange(a.PeriodSeconds!.Value, 1.8, 2.4);
        Assert.Equal(327, a.VrrCapFps);
        var startup = new List<double>();
        for (var i = 0; i < 3000; i++) startup.Add(i < 400 && i % 20 == 0 ? 50 : 7);
        Assert.Equal("startup", StutterAnalyzer.Analyze(startup).Pattern);
        Assert.Equal("none", StutterAnalyzer.Analyze(Enumerable.Repeat(7.0, 500).ToList()).Pattern);
    }

    [Fact]
    public void Slowdowns_since_the_first_investigation_are_explained()
    {
        var then = new SystemSnapshot { Utc = DateTime.UtcNow.AddMonths(-4), RamSpeedMts = 6000, StartupNames = { "Steam" }, WakeupsPerSec = 4000, SystemFreePercent = 60, GpuDriver = "32.0.15.6094" };
        var now = new SystemSnapshot { RamSpeedMts = 4800, StartupNames = { "Steam", "OneDrive", "Spotify", "Armoury Crate" }, WakeupsPerSec = 9000, SystemFreePercent = 12, GpuDriver = "32.0.15.6614" };
        var f = DetectiveAnalysis.Degradation(then, now, Array.Empty<BenchmarkManager.QuickBench.QuickBenchResult>());
        Assert.Contains(f, x => x.Id == "deg.ram" && x.Severity == Severity.Warning);
        Assert.Contains(f, x => x.Id == "deg.startup");
        Assert.Contains(f, x => x.Id == "deg.wakeups");
        Assert.Contains(f, x => x.Id == "deg.disk");
        Assert.Contains(f, x => x.Id == "deg.driver" && x.Fix == DetectiveFix.CleanShaderCache);
        Assert.Contains(DetectiveAnalysis.Degradation(null, now, Array.Empty<BenchmarkManager.QuickBench.QuickBenchResult>()), x => x.Id == "deg.baseline");
    }

    [Fact]
    public void Hardware_checks_catch_a_card_on_too_few_lanes_and_a_full_drive()
    {
        var p = new MachineProfile
        {
            Gpus = { new GpuInfo { Name = "RTX 4070", PcieCurrentWidth = 4, PcieMaxWidth = 16, ResizableBar = ResizableBarState.Disabled } },
            Storage = { new StorageDevice { FriendlyName = "NVMe", Volumes = { new VolumeInfo { Letter = "C:", SizeBytes = 1000, FreeBytes = 50, IsSystem = true } } } },
        };
        var f = DetectiveAnalysis.Hardware(p);
        Assert.Contains(f, x => x.Id == "hw.pcieWidth" && x.Severity == Severity.Critical);
        Assert.Contains(f, x => x.Id == "hw.rebar");
        Assert.Contains(f, x => x.Id == "hw.full.C:" && x.Severity == Severity.Warning);
    }
}
