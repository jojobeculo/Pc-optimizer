using FrameCore.BenchmarkManager.Games;
using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;
using Xunit;

namespace FrameCore.Tests;

public class GameEstimatorTests
{
    private static readonly GameLibrary Lib = GameLibrary.Load(Path.Combine(Repo.Catalog, "knowledge"));

    private static EstimatorMachine Machine(string gpu, string cpu, double vram = 12, double ram = 32, ChannelMode ch = ChannelMode.Dual) =>
        new() { GpuName = gpu, CpuName = cpu, GpuVramGb = vram, RamGb = ram, Channels = ch, Xmp = XmpState.LikelyEnabled };

    [Fact]
    public void Library_is_complete_and_coherent()
    {
        Assert.InRange(Lib.Games.Count, 50, 100);
        Assert.Equal(Lib.Games.Count, Lib.Games.Select(g => g.Id).Distinct().Count());
        Assert.All(Lib.Games, g =>
        {
            Assert.True(g.RefFps > 0 && g.CpuCeilingFps > 0, g.Id);
            Assert.InRange(g.ResolutionExponent, 0.1, 1.0);
            Assert.InRange(g.CpuSensitivity, 0, 1);
        });
        Assert.Contains(Lib.Excluded, e => e.Name.Contains("VI"));
        Assert.True(Lib.Gpus.Count > 80 && Lib.Cpus.Count > 60);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "RTX 4070 SUPER")]
    [InlineData("NVIDIA GeForce RTX 4070", "RTX 4070")]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "RTX 4060 Laptop")]
    [InlineData("AMD Radeon RX 7800 XT", "RX 7800 XT")]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", "Arc B580")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", "GTX 1060 6GB")]
    public void Gpu_names_are_matched_to_the_most_specific_entry(string name, string expected)
    {
        var (gpu, q) = Lib.MatchGpu(name);
        Assert.Equal(expected, gpu?.Name);
        Assert.Equal(MatchQuality.Exact, q);
    }

    [Fact]
    public void Ambiguous_integrated_radeon_is_resolved_through_the_apu_and_flagged_approximate()
    {
        var (gpu, q) = Lib.MatchGpu("AMD Radeon(TM) Graphics", "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics");
        Assert.Equal("Radeon 780M", gpu?.Name);
        Assert.Equal(MatchQuality.Approximate, q);
        Assert.Null(Lib.MatchGpu("Microsoft Basic Render Driver").gpu);
    }

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600KF", "Core i5-13600K")]
    [InlineData("Intel(R) Core(TM) i5-12400F", "Core i5-12400")]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "Ryzen 7 7800X3D")]
    [InlineData("AMD Ryzen 5 7600X 6-Core Processor", "Ryzen 5 7600X")]
    [InlineData("AMD Ryzen 5 7600 6-Core Processor", "Ryzen 5 7600")]
    public void Cpu_names_are_matched(string name, string expected) => Assert.Equal(expected, Lib.MatchCpu(name).cpu?.Name);

    [Fact]
    public void Reference_machine_reproduces_the_reference_point_and_scales_sensibly()
    {
        Repo.LoadLoc();
        var est = new FpsEstimator(Lib);
        var m = Machine("NVIDIA GeForce RTX 4070", "AMD Ryzen 7 7800X3D 8-Core Processor");
        var cp = Lib.Find("cyberpunk2077")!;
        var q1440 = est.Estimate(cp, m, new EstimateSettings(2560, 1440, GraphicsPreset.High));
        Assert.InRange(q1440.Fps!.Value, cp.RefFps * 0.9, cp.RefFps * 1.01);
        Assert.Equal(Limiter.Gpu, q1440.Limiter);
        Assert.True(q1440.FpsLow < q1440.Fps && q1440.FpsHigh > q1440.Fps, "an estimate is always a range");
        Assert.NotEqual(ResultConfidence.High, q1440.Confidence); // never 'high' without a real measurement
        var q4k = est.Estimate(cp, m, new EstimateSettings(3840, 2160, GraphicsPreset.High));
        var q1080low = est.Estimate(cp, m, new EstimateSettings(1920, 1080, GraphicsPreset.Low));
        Assert.True(q4k.Fps < q1440.Fps && q1080low.Fps > q1440.Fps);
    }

    [Fact]
    public void Caps_vram_and_memory_configuration_are_taken_into_account()
    {
        Repo.LoadLoc();
        var est = new FpsEstimator(Lib);
        var fast = Machine("NVIDIA GeForce RTX 4090", "AMD Ryzen 7 9800X3D 8-Core Processor", vram: 24);
        Assert.True(est.Estimate(Lib.Find("eldenring")!, fast, new(1920, 1080, GraphicsPreset.Low)).Fps <= 60);

        var small = Machine("NVIDIA GeForce RTX 4060", "AMD Ryzen 7 7800X3D 8-Core Processor", vram: 8);
        var e = est.Estimate(Lib.Find("indianajones")!, small, new(3840, 2160, GraphicsPreset.Ultra));
        Assert.Equal(Limiter.Vram, e.Limiter);
        Assert.Contains("game.note.vram", e.NoteKeys);

        var single = Machine("NVIDIA GeForce RTX 4090", "AMD Ryzen 5 5600 6-Core Processor", vram: 24, ch: ChannelMode.Single);
        var dual = Machine("NVIDIA GeForce RTX 4090", "AMD Ryzen 5 5600 6-Core Processor", vram: 24);
        var cs2 = Lib.Find("cs2")!;
        Assert.True(est.Estimate(cs2, single, new(1920, 1080, GraphicsPreset.Low)).Fps < est.Estimate(cs2, dual, new(1920, 1080, GraphicsPreset.Low)).Fps);
    }

    [Fact]
    public void Unknown_gpu_or_missing_ray_tracing_gives_no_number()
    {
        var est = new FpsEstimator(Lib);
        Assert.Null(est.Estimate(Lib.Find("fortnite")!, Machine("Microsoft Basic Render Driver", "Intel Xeon"), new(1920, 1080, GraphicsPreset.Low)).Fps);
        var gtx = Machine("NVIDIA GeForce GTX 1080 Ti", "AMD Ryzen 7 7800X3D 8-Core Processor") ;
        gtx.GpuSupportsRayTracing = false;
        Assert.Equal(EstimateBasis.Insufficient, est.Estimate(Lib.Find("doomtda")!, gtx, new(1920, 1080, GraphicsPreset.Low)).Basis);
    }

    [Fact]
    public void A_real_measurement_replaces_then_calibrates_the_model()
    {
        var m = Machine("NVIDIA GeForce RTX 4070", "AMD Ryzen 7 7800X3D 8-Core Processor");
        var g = Lib.Find("cyberpunk2077")!;
        var est = new FpsEstimator(Lib, new[] { new GameMeasurement(g.Id, 2560, 1440, GraphicsPreset.High, 100, 80, DateTime.UtcNow) });
        var same = est.Estimate(g, m, new(2560, 1440, GraphicsPreset.High));
        Assert.Equal(EstimateBasis.Measured, same.Basis);
        Assert.Equal(100, same.Fps);
        var other = est.Estimate(g, m, new(1920, 1080, GraphicsPreset.High));
        Assert.Equal(EstimateBasis.CalibratedByMeasurement, other.Basis);
        Assert.True(other.Fps > new FpsEstimator(Lib).Estimate(g, m, new(1920, 1080, GraphicsPreset.High)).Fps);
    }

    private static QuickBenchComparison Cmp(string key, double improvement, ChangeVerdict v) => new()
    {
        Metrics = { new MetricComparison { Key = key, Component = BenchComponent.Cpu, ImprovementPercent = improvement, Verdict = v } },
    };

    [Fact]
    public void Gains_come_only_from_measured_changes_and_never_from_network_tweaks()
    {
        var est = new FpsEstimator(Lib);
        var weakCpu = Machine("NVIDIA GeForce RTX 4090", "Intel(R) Core(TM) i5-10400", vram: 24);
        var before = est.Estimate(Lib.Find("cs2")!, weakCpu, new(1920, 1080, GraphicsPreset.Low));
        Assert.Equal(Limiter.Cpu, before.Limiter);

        var network = new TweakDefinition { Category = "Network", ImpactAreas = { ImpactArea.NetworkLatency } };
        var cpuTweak = new TweakDefinition { Category = "Cpu", ImpactAreas = { ImpactArea.Fps } };

        var none = FpsEstimator.ProjectGain(before, null, new[] { network });
        Assert.Equal(0, none.GainPercent);
        Assert.Contains("game.gain.noFpsTweaks", none.ReasonKeys);

        Assert.Null(FpsEstimator.ProjectGain(before, null, new[] { cpuTweak }).GainPercent); // needs a before/after benchmark

        var noisy = FpsEstimator.ProjectGain(before, Cmp("cpu.single", 1.2, ChangeVerdict.WithinNoise), new[] { cpuTweak });
        Assert.Equal(0, noisy.GainPercent);

        var real = FpsEstimator.ProjectGain(before, Cmp("cpu.single", 10, ChangeVerdict.Improved), new[] { cpuTweak });
        Assert.InRange(real.GainPercent!.Value, 3, 7);
        Assert.True(real.AfterFps > before.Fps);

        var gpuBound = est.Estimate(Lib.Find("cyberpunk2077")!, Machine("NVIDIA GeForce RTX 3060", "AMD Ryzen 7 9800X3D 8-Core Processor"), new(3840, 2160, GraphicsPreset.Ultra));
        var cpuGainOnGpuBound = FpsEstimator.ProjectGain(gpuBound, Cmp("cpu.single", 10, ChangeVerdict.Improved), new[] { cpuTweak });
        Assert.True(cpuGainOnGpuBound.GainPercent < 0.5, "a CPU gain barely helps a GPU-bound game");
    }

    [Fact]
    public void Showcase_picks_three_distinct_relevant_games()
    {
        var est = new FpsEstimator(Lib);
        var picks = est.PickShowcase(Machine("NVIDIA GeForce RTX 4070", "AMD Ryzen 7 7800X3D 8-Core Processor"), 2560, 1440);
        Assert.Equal(3, picks.Select(p => p.Game.Id).Distinct().Count());
        Assert.All(picks, p => Assert.NotNull(p.Fps));
    }
}
