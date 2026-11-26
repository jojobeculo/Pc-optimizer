using FrameCore.BenchmarkManager;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.CpuManager;
using FrameCore.DisplayManager;
using FrameCore.GpuManager;
using FrameCore.HardwareDetection;
using FrameCore.MemoryManager;
using FrameCore.StartupManager;
using FrameCore.WindowsOptimization.Executors;
using Xunit;

namespace FrameCore.Tests;

public class FrameStatisticsTests
{
    [Fact]
    public void Constant_frame_times_give_exact_fps()
    {
        var s = FrameStatistics.Compute(Enumerable.Repeat(1000.0 / 60, 600).ToList());
        Assert.Equal(60, s.AverageFps, 3);
        Assert.Equal(60, s.Low1PercentFps, 3);
        Assert.Equal(0, s.FrameTimeStdDevMs, 6);
        Assert.Null(s.Low01PercentFps); // < 10,000 frames
        Assert.Equal(0, s.Stutters);
    }

    [Fact]
    public void One_percent_low_is_the_average_of_the_slowest_one_percent()
    {
        var frames = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(40.0, 10)).ToList();
        var s = FrameStatistics.Compute(frames);
        Assert.Equal(25.0, s.Low1PercentFps, 6);                   // 1000 / 40
        Assert.Equal(1000 / ((990 * 10.0 + 10 * 40.0) / 1000), s.AverageFps, 6); // frames / total time
        Assert.Equal(10, s.Stutters);
    }

    [Fact]
    public void Point_one_percent_low_needs_ten_thousand_frames()
    {
        var frames = Enumerable.Repeat(5.0, 9990).Concat(Enumerable.Repeat(50.0, 10)).ToList();
        var s = FrameStatistics.Compute(frames);
        Assert.NotNull(s.Low01PercentFps);
        Assert.Equal(20.0, s.Low01PercentFps!.Value, 6);
    }

    [Fact]
    public void Percentile_interpolates_like_excel()
    {
        var sorted = new double[] { 1, 2, 3, 4 };
        Assert.Equal(2.5, FrameStatistics.Percentile(sorted, 50), 9);
        Assert.Equal(3.97, FrameStatistics.Percentile(sorted, 99), 9);
    }

    [Fact]
    public void Timestamps_become_frame_times()
    {
        Assert.Equal(new[] { 10.0, 15.0 }, FrameStatistics.FromTimestamps(new[] { 0.0, 10.0, 25.0 }));
    }
}

public class PresentMonTests
{
    [Fact]
    public void Parses_v1_and_v2_columns_and_skips_NA()
    {
        var csv = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,MsBetweenPresents,MsBetweenDisplayChange\n" +
                  "game.exe,100,0x1,DXGI,0,0,1,Hardware: Independent Flip,16.5,16.6\n" +
                  "game.exe,100,0x1,DXGI,0,0,1,Hardware: Independent Flip,NA,NA\n" +
                  "game.exe,100,0x1,DXGI,0,0,1,Hardware: Independent Flip,17.0,16.6\n" +
                  "dwm.exe,50,0x2,Other,0,0,0,Composed: Flip,16.6,16.6\n";
        var caps = PresentMonCsvParser.Parse(new StringReader(csv));
        Assert.Equal(2, caps.Count);
        Assert.Equal("game.exe", caps[0].Application);
        Assert.Equal(new[] { 16.5, 17.0 }, caps[0].FrameTimesMs);
        Assert.Equal("DXGI", caps[0].Runtime);
    }

    [Fact]
    public void Missing_frame_time_column_is_reported()
    {
        Assert.Throws<FormatException>(() => PresentMonCsvParser.Parse(new StringReader("Application,ProcessID\nx,1\n")));
    }
}

public class ComparisonTests
{
    private static BenchmarkSession Session(params double[] fps) => new()
    {
        Runs = fps.Select(f => new BenchmarkRun { Stats = new FrameStats { AverageFps = f, Low1PercentFps = f * 0.7, P99FrameTimeMs = 1000 / (f * 0.7), FrameTimeStdDevMs = 1 } }).ToList(),
    };

    [Fact]
    public void Welch_test_detects_real_differences_only()
    {
        Assert.True(SessionComparer.WelchTTest(new[] { 100.0, 101, 99, 100.5 }, new[] { 110.0, 111, 109, 110.5 }) < 0.001);
        Assert.True(SessionComparer.WelchTTest(new[] { 100.0, 102, 98, 101 }, new[] { 101.0, 99, 100, 102 }) > 0.3);
    }

    [Fact]
    public void Small_differences_with_few_runs_are_not_claimed_as_gains()
    {
        Repo.LoadLoc();
        var c = SessionComparer.Compare(Session(100), Session(101)).First();
        Assert.Equal("withinNoise", c.Verdict);
        var big = SessionComparer.Compare(Session(100, 101, 99), Session(120, 121, 119)).First();
        Assert.Equal("significant", big.Verdict);
        Assert.Equal(20, big.DeltaPercent, 1);
    }
}

public class BottleneckTests
{
    private static List<SensorPoint> Samples(double gpu, double maxCore, double cpu = 30, double? vram = null) =>
        Enumerable.Range(0, 30).Select(i => new SensorPoint { T = i, Gpu = gpu, CpuMaxCore = maxCore, Cpu = cpu, VramUsedGb = vram, RamUsedGb = 10 }).ToList();

    [Fact]
    public void Gpu_bound_capture()
    {
        Repo.LoadLoc();
        var f = BottleneckAnalyzer.Analyze(new BottleneckInput { Target = "game", DurationSeconds = 30, Samples = Samples(98, 60), RefreshHz = 144, Frames = new FrameStats { Frames = 3000, AverageFps = 97, FrameTimeStdDevMs = 2 } });
        Assert.Contains(f, x => x.Id == "bottleneck.gpu");
        Assert.DoesNotContain(f, x => x.Id == "bottleneck.cpu");
        Assert.All(f, x => Assert.False(string.IsNullOrEmpty(x.Context)));
    }

    [Fact]
    public void Cpu_bound_and_vram_saturation()
    {
        Repo.LoadLoc();
        var f = BottleneckAnalyzer.Analyze(new BottleneckInput { Target = "game", DurationSeconds = 30, Samples = Samples(60, 97, 40, vram: 7.9), VramTotalGb = 8, RefreshHz = 144, Frames = new FrameStats { Frames = 3000, AverageFps = 80, FrameTimeStdDevMs = 3 } });
        Assert.Contains(f, x => x.Id == "bottleneck.cpu");
        Assert.Contains(f, x => x.Id == "bottleneck.vram");
    }

    [Fact]
    public void Capped_fps_is_not_called_a_hardware_bottleneck()
    {
        Repo.LoadLoc();
        var f = BottleneckAnalyzer.Analyze(new BottleneckInput { Target = "game", DurationSeconds = 30, Samples = Samples(55, 40), RefreshHz = 144, Frames = new FrameStats { Frames = 4000, AverageFps = 143.8, FrameTimeStdDevMs = 0.2 } });
        Assert.Contains(f, x => x.Id == "bottleneck.capped");
        Assert.DoesNotContain(f, x => x.Id is "bottleneck.gpu" or "bottleneck.cpu");
    }
}

public class EdidTests
{
    private static byte[] BuildEdid()
    {
        var e = new byte[256];
        new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 }.CopyTo(e, 0);
        // "GSM": G=7, S=19, M=13 → (7<<10)|(19<<5)|13
        var mfg = (7 << 10) | (19 << 5) | 13;
        e[8] = (byte)(mfg >> 8); e[9] = (byte)(mfg & 0xFF);
        e[10] = 0x7F; e[11] = 0x5B; e[18] = 1; e[19] = 4;
        e[24] = 0x01; // continuous frequency
        // DTD 2560x1440 @ ~144 Hz: pixel clock 586.59 MHz, h total 2720 (blank 160), v total 1497 (blank 57)
        var clock = 58659; // in 10 kHz
        int o = 54;
        e[o] = (byte)(clock & 0xFF); e[o + 1] = (byte)(clock >> 8);
        e[o + 2] = 2560 & 0xFF; e[o + 3] = 160; e[o + 4] = (byte)(((2560 >> 8) << 4) | 0);
        e[o + 5] = 1440 & 0xFF; e[o + 6] = 57; e[o + 7] = (byte)(((1440 >> 8) << 4) | 0);
        // Range limits 48–144 Hz
        o = 72; e[o + 3] = 0xFD; e[o + 5] = 48; e[o + 6] = 144;
        // Name
        o = 90; e[o + 3] = 0xFC; "LG ULTRAGEAR\n"u8.ToArray().CopyTo(e, o + 5);
        e[126] = 1;
        // CTA extension with HDR static metadata (ext tag 6) and AMD VSDB (FreeSync 48-144)
        var b = 128;
        e[b] = 0x02; e[b + 1] = 0x03;
        var i = b + 4;
        e[i++] = (7 << 5) | 3; e[i++] = 6; e[i++] = 0x05; e[i++] = 0x01;          // HDR SMDB: EOTF SDR+PQ
        e[i++] = (3 << 5) | 8; e[i++] = 0x1A; e[i++] = 0x00; e[i++] = 0x00;     // AMD OUI 00001A
        e[i++] = 0x01; e[i++] = 0x01; e[i++] = 0x01; e[i++] = 48; e[i++] = 144; // version, ?, feature(bit0), min, max
        e[b + 2] = (byte)(i - b);
        return e;
    }

    [Fact]
    public void Parses_identity_timing_range_hdr_and_freesync()
    {
        var info = EdidParser.Parse(BuildEdid());
        Assert.True(info.Valid);
        Assert.Equal("GSM", info.ManufacturerCode);
        Assert.Equal("LG ULTRAGEAR", info.MonitorName);
        Assert.Equal(2560, info.PreferredWidth);
        Assert.Equal(1440, info.PreferredHeight);
        Assert.InRange(info.PreferredRefreshHz!.Value, 143.5, 144.5);
        Assert.Equal(48, info.RangeMinVHz);
        Assert.Equal(144, info.RangeMaxVHz);
        Assert.True(info.HdrStaticMetadata);
        Assert.True(info.HdrPq);
        Assert.True(info.FreeSync);
        Assert.Equal(48, info.VrrMin);
        Assert.Equal(144, info.VrrMax);
        Assert.True(info.AnyVrr);
    }

    [Fact]
    public void Invalid_edid_is_rejected()
    {
        Assert.False(EdidParser.Parse(new byte[128]).Valid);
    }
}

public class MemoryTests
{
    [Theory]
    [InlineData("CMK16GX4M2B3200C16", 3200)]
    [InlineData("CMH32GX5M2E6000C36", 6000)]
    [InlineData("F4-3600C16D-16GTZNC", 3600)]
    [InlineData("F5-6000J3038F16GX2-TZ5RK", 6000)]
    [InlineData("KF432C16BBK2/16", 3200)]
    [InlineData("KF560C36BBEK2-32", 6000)]
    [InlineData("BL2K16G36C16U4B", 3600)]
    [InlineData("CP2K16G60C36U5B", 6000)]
    [InlineData("TF3D416G3200HC16FDC01", 3200)]
    [InlineData("AX5U6000C3016G-DCLARBK", 6000)]
    [InlineData("M378A1K43EB2-CWE", null)]
    [InlineData(null, null)]
    public void Decodes_known_kit_ratings_and_never_guesses(string? part, int? expected) =>
        Assert.Equal(expected, RamPartNumberDecoder.Decode(part));

    [Fact]
    public void Detects_xmp_off_and_single_channel()
    {
        Repo.LoadLoc();
        var mem = new MemoryInfo
        {
            Modules = { new MemoryModule { Slot = "DIMM_A2", PartNumber = "F5-6000J3038F16GX2-TZ5RK", ConfiguredSpeedMts = 4800, Type = "DDR5", FormFactor = "DIMM" } },
        };
        MemoryConfigAnalyzer.Analyze(mem, laptop: false);
        Assert.Equal(XmpState.LikelyDisabled, mem.Xmp);
        Assert.Equal(ChannelMode.Single, mem.Channels);
        Assert.Equal(2400, mem.ClockMhz); // MT/s vs MHz distinction
    }

    [Fact]
    public void Detects_dual_channel_from_slot_names_and_xmp_on()
    {
        Repo.LoadLoc();
        var mem = new MemoryInfo
        {
            Modules =
            {
                new MemoryModule { Slot = "Controller0-ChannelA-DIMM1", PartNumber = "CMK16GX4M2B3200C16", ConfiguredSpeedMts = 3200, Type = "DDR4" },
                new MemoryModule { Slot = "Controller0-ChannelB-DIMM1", PartNumber = "CMK16GX4M2B3200C16", ConfiguredSpeedMts = 3200, Type = "DDR4" },
            },
        };
        MemoryConfigAnalyzer.Analyze(mem, laptop: false);
        Assert.Equal(ChannelMode.Dual, mem.Channels);
        Assert.Equal(XmpState.LikelyEnabled, mem.Xmp);
    }
}

public class IdentificationTests
{
    [Fact]
    public void Cpu_identification()
    {
        Assert.Equal("Raptor Lake", Identification.IntelMicroarchitecture(6, 0xB7));
        Assert.Equal("Alder Lake", Identification.IntelMicroarchitecture(6, 0x97));
        Assert.Equal("Zen 4", Identification.AmdMicroarchitecture(0x19, 0x61));
        Assert.Equal("Zen 3", Identification.AmdMicroarchitecture(0x19, 0x21));
        Assert.Equal("Zen 5", Identification.AmdMicroarchitecture(0x1A, 0x44));
        Assert.Null(Identification.IntelMicroarchitecture(6, 0x01));
        Assert.Equal("Core 14th gen", Identification.Generation(CpuVendor.Intel, "Intel(R) Core(TM) i7-14700K"));
        Assert.Equal("Core Ultra Series 2", Identification.Generation(CpuVendor.Intel, "Intel(R) Core(TM) Ultra 9 285K"));
        Assert.Equal("Ryzen 7000", Identification.Generation(CpuVendor.Amd, "AMD Ryzen 7 7800X3D 8-Core Processor"));
        Assert.True(Identification.IsDualCcdX3D("AMD Ryzen 9 7950X3D 16-Core Processor", 2, 16));
        Assert.False(Identification.IsDualCcdX3D("AMD Ryzen 7 9800X3D 8-Core Processor", 1, 8));
        Assert.Equal(TriState.Yes, Identification.IsUnlocked(CpuVendor.Intel, "Intel Core i9-13900KF", false));
        Assert.Equal(TriState.No, Identification.IsUnlocked(CpuVendor.Intel, "Intel Core i5-13400F", false));
        Assert.True(Identification.IsMobileCpu("Intel Core i7-13700H"));
        Assert.False(Identification.IsMobileCpu("Intel Core i7-13700K"));
    }

    [Fact]
    public void Gpu_and_board_identification()
    {
        Assert.Equal(GpuVendor.Nvidia, Identification.GpuVendorFrom(0x10DE, null));
        Assert.Equal("Ada Lovelace (RTX 40)", Identification.GpuArchitecture(GpuVendor.Nvidia, "NVIDIA GeForce RTX 4070 Ti"));
        Assert.Equal("RDNA 4 (RX 9000)", Identification.GpuArchitecture(GpuVendor.Amd, "AMD Radeon RX 9070 XT"));
        Assert.True(Identification.ResizableBarCapableGpu(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060"));
        Assert.False(Identification.ResizableBarCapableGpu(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1080"));
        Assert.Equal("B650E", Identification.ChipsetFromBoard("ROG STRIX B650E-F GAMING WIFI"));
        Assert.Equal("Z790", Identification.ChipsetFromBoard("MAG Z790 TOMAHAWK WIFI"));
        Assert.Equal("B550", Identification.ChipsetFromBoard("B550M AORUS ELITE"));
        Assert.Null(Identification.ChipsetFromBoard("Standard"));
        Assert.Equal("DDR5", Identification.MemoryType(34));
    }

    [Fact]
    public void Windows_servicing_uses_build_and_edition()
    {
        Repo.LoadLoc();
        var today = new DateTime(2026, 11, 26);
        Assert.True(WindowsVersions.Evaluate(26100, "Professional", today).ended);              // 24H2 Home/Pro ended 2026-10-13
        Assert.False(WindowsVersions.Evaluate(26100, "Enterprise", today).ended);               // until 2027-10-12
        Assert.False(WindowsVersions.Evaluate(26200, "Core", today).ended);                     // 25H2
        Assert.Equal("26H2", WindowsVersions.Evaluate(26300, "Professional", today).version);
        Assert.False(WindowsVersions.Evaluate(29999, "Professional", today).ended);             // newer than table
    }
}

public class StartupTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tool.exe -silent", "C:\\Tools\\tool.exe")]
    [InlineData("rundll32.exe C:\\x\\y.dll,Entry", "rundll32.exe")]
    public void Extracts_executable_path(string command, string expected) =>
        Assert.Equal(expected, StartupScanner.ExtractPath(command));

    [Fact]
    public void Approval_blob_semantics_match_task_manager()
    {
        Assert.True(StartupScanner.IsApproved(null));
        Assert.True(StartupScanner.IsApproved(Convert.FromHexString("020000000000000000000000")));
        Assert.False(StartupScanner.IsApproved(Convert.FromHexString("030000000000000000000000")));
        Assert.False(StartupScanner.IsApproved(Convert.FromHexString("070000000000000000000000")));
    }

    [Fact]
    public void Knowledge_base_protects_anticheats_and_flags_updaters()
    {
        Repo.LoadLoc();
        var kb = StartupKnowledgeBase.Load(Path.Combine(Repo.Catalog, "knowledge", "startup-rules.json"));
        var vgc = new StartupEntry { Name = "vgc", Kind = StartupKind.Service };
        kb.Classify(vgc);
        Assert.Equal(StartupClassification.Essential, vgc.Classification);
        Assert.True(vgc.Protected);
        var gupdate = new StartupEntry { Name = "GoogleUpdateTaskMachineCore", Kind = StartupKind.ScheduledTask, FilePath = @"C:\Program Files\Google\Update\GoogleUpdate.exe" };
        kb.Classify(gupdate);
        Assert.Equal(StartupClassification.Optional, gupdate.Classification);
        var steam = new StartupEntry { Name = "Steam", Kind = StartupKind.RunKey, FilePath = @"C:\Program Files (x86)\Steam\steam.exe" };
        kb.Classify(steam);
        Assert.Equal("launcher", steam.Category);
        var suspicious = new StartupEntry { Name = "xk3j2", Kind = StartupKind.RunKey, FilePath = @"C:\Users\a\AppData\Local\Temp\xk3j2.exe", FileExists = true };
        kb.Classify(suspicious);
        Assert.Equal(StartupClassification.Risky, suspicious.Classification);
    }

    [Fact]
    public void Disable_action_uses_startup_approved_not_deletion()
    {
        var e = new StartupEntry { Kind = StartupKind.RunKey, Name = "Spotify", ApprovalPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", CanToggle = true };
        var a = StartupController.BuildAction(e, enable: false)!;
        Assert.Equal("registry", a.Type);
        Assert.Equal("Spotify", a.Name);
        Assert.Equal("030000000000000000000000", a.ValueAsString());
        Assert.False(a.Delete);
    }
}

public class StressAndTuningTests
{
    [Fact]
    public void Cpu_work_unit_is_deterministic_and_short_run_has_no_error()
    {
        Assert.Equal(CpuStressTest.WorkUnit(5), CpuStressTest.WorkUnit(5));
        Assert.NotEqual(CpuStressTest.WorkUnit(5), CpuStressTest.WorkUnit(6));
        var r = CpuStressTest.Run(TimeSpan.FromMilliseconds(600), 2, null, CancellationToken.None);
        Assert.Equal(0, r.Errors);
        Assert.True(r.Iterations > 0);
    }

    private sealed class FakeGpu : IGpuTuningChannel, IGpuStabilityTester
    {
        public int Core, Mem;
        public int CoreLimit = 95, MemLimit = 300;
        public bool FailFinal;
        public List<int> AppliedCores { get; } = new();
        public GpuTuningCapabilities Capabilities(int index) => new() { Available = true, CoreOffset = true, CoreMin = -200, CoreMax = 250, MemOffset = true, MemMin = -500, MemMax = 1000 };
        public GpuTuningState Read(int index) => new() { CoreOffsetMhz = Core, MemOffsetMhz = Mem };
        public void Apply(GpuTuningState s) { if (s.CoreOffsetMhz is { } c) { Core = c; AppliedCores.Add(c); } if (s.MemOffsetMhz is { } m) Mem = m; }
        public GpuStabilityResult Run(TimeSpan d, CancellationToken ct)
        {
            if (FailFinal && d > TimeSpan.FromSeconds(100)) return new GpuStabilityResult { ComputeErrors = 1 };
            return new GpuStabilityResult
            {
                ComputeErrors = Core > CoreLimit ? 3 : 0,
                // Above the memory limit GDDR error correction lowers bandwidth instead of failing.
                BandwidthGBs = Mem <= MemLimit ? 400 + Mem * 0.05 : 380,
                MaxTemperatureC = 70,
            };
        }
    }

    [Fact]
    public void Auto_tuner_steps_back_from_the_first_failure_and_respects_the_cap()
    {
        Repo.LoadLoc();
        var gpu = new FakeGpu();
        var tuner = new GpuAutoTuner(gpu, gpu);
        var r = tuner.Run(0, new AutoTuneOptions { CoreCapMhz = 150, StepDuration = TimeSpan.FromSeconds(1), FinalValidation = TimeSpan.FromSeconds(2) }, null, CancellationToken.None);
        Assert.True(r.Success, r.Message);
        Assert.Equal(75, r.FinalCore);              // 90 passed, 105 failed → one step back from 90
        Assert.Equal(250, r.FinalMem);              // 300 passed, 350 regressed → one step back
        Assert.All(gpu.AppliedCores, c => Assert.True(c <= 150));
    }

    [Fact]
    public void Auto_tuner_restores_baseline_when_final_validation_fails()
    {
        Repo.LoadLoc();
        var gpu = new FakeGpu { FailFinal = true, Core = 10 };
        var r = new GpuAutoTuner(gpu, gpu).Run(0, new AutoTuneOptions { StepDuration = TimeSpan.FromSeconds(1), FinalValidation = TimeSpan.FromSeconds(200), TuneMemory = false }, null, CancellationToken.None);
        Assert.False(r.Success);
        Assert.Equal(10, gpu.Core);
    }
}

public class PlatformParsingTests
{
    [Fact]
    public void Bcdedit_output_is_parsed_independently_of_language()
    {
        var output = "Chargeur de démarrage Windows\r\n--------------------------------\r\nidentificateur          {current}\r\nuseplatformclock        Oui\r\ndisabledynamictick      Non\r\n";
        var map = BcdRemoveExecutor.ParseCurrentEntry(output);
        Assert.Equal("Oui", map["useplatformclock"]);
        Assert.Equal("true", BcdRemoveExecutor.NormalizeValue(map["useplatformclock"]));
        Assert.Equal("false", BcdRemoveExecutor.NormalizeValue(map["disabledynamictick"]));
    }

    [Fact]
    public void Gpu_engine_counters_are_aggregated_per_adapter()
    {
        var raw = new Dictionary<string, double>
        {
            ["pid_100_luid_0x00000000_0x0000C8A1_phys_0_eng_0_engtype_3D"] = 60,
            ["pid_200_luid_0x00000000_0x0000C8A1_phys_0_eng_0_engtype_3D"] = 25,
            ["pid_100_luid_0x00000000_0x0000C8A1_phys_0_eng_5_engtype_Copy"] = 10,
        };
        var agg = GpuMonitor.AggregateEngines(raw);
        var luid = GpuMonitor.ParseLuid("luid_0x00000000_0x0000C8A1");
        Assert.Equal(0xC8A1, luid);
        Assert.Equal(85, agg[luid]["3D"]);
        Assert.Equal(10, agg[luid]["Copy"]);
    }

    [Fact]
    public void Registry_codec_round_trips()
    {
        Assert.Equal("4294967295", RegistryCodec.Encode(new RegValue("DWord", unchecked((int)0xFFFFFFFF))));
        Assert.Equal(unchecked((int)0xFFFFFFFF), RegistryCodec.Decode("DWord", "0xFFFFFFFF"));
        Assert.True(RegistryCodec.AreEqual("DWord", "0x26", "38"));
        Assert.Equal(new byte[] { 3, 0 }, RegistryCodec.Decode("Binary", "0300"));
        Assert.Equal(new[] { "a", "b" }, (string[])RegistryCodec.Decode("MultiString", RegistryCodec.Encode(new RegValue("MultiString", new[] { "a", "b" }))));
    }

    [Fact]
    public void DirectX_settings_string_is_parsed_and_formatted()
    {
        var d = DxSettingExecutor.Parse("SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;");
        Assert.Equal("1", d["SwapEffectUpgradeEnable"]);
        Assert.Equal("SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;", DxSettingExecutor.Format(d));
    }
}
