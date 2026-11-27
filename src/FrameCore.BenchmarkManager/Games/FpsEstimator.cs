using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Games;

public enum GraphicsPreset { Low, Medium, High, Ultra }

/// <summary>Where a number comes from — always shown to the user.</summary>
public enum EstimateBasis
{
    /// <summary>Measured on this machine (in-game capture at these settings).</summary>
    Measured,
    /// <summary>Model scaled by a real measurement of this game on this machine at other settings.</summary>
    CalibratedByMeasurement,
    /// <summary>Model with exactly identified hardware.</summary>
    Theoretical,
    /// <summary>Model with approximately identified hardware (similar chip).</summary>
    ExtrapolatedFromSimilar,
    /// <summary>Not enough data to say anything useful.</summary>
    Insufficient,
}

public enum Limiter { Gpu, Cpu, Balanced, Vram, FrameCap, Unknown }

/// <summary>A real in-game measurement used to calibrate (from the in-game capture).</summary>
public sealed record GameMeasurement(string GameId, int Width, int Height, GraphicsPreset Preset, double AvgFps, double? Low1Fps, DateTime Utc);

public sealed record EstimateSettings(int Width, int Height, GraphicsPreset Preset);

public sealed class GameEstimate
{
    public GameInfo Game { get; init; } = null!;
    public EstimateSettings Settings { get; init; } = null!;
    public double? Fps { get; set; }
    public double? FpsLow { get; set; }
    public double? FpsHigh { get; set; }
    public Limiter Limiter { get; set; }
    public ResultConfidence Confidence { get; set; }
    public EstimateBasis Basis { get; set; }
    public double? GpuBoundFps { get; set; }
    public double? CpuBoundFps { get; set; }
    /// <summary>Elasticities: share of the frame rate governed by the GPU / CPU (sum = 1).</summary>
    public double GpuShare { get; set; }
    public double CpuShare { get; set; }
    public List<string> NoteKeys { get; } = new();
}

/// <summary>Projected effect of the measured before/after benchmark change on one game.</summary>
public sealed class GameGain
{
    public GameEstimate Before { get; init; } = null!;
    public double? AfterFps { get; set; }
    public double? GainPercent { get; set; }
    public double? GainPercentLow { get; set; }
    public double? GainPercentHigh { get; set; }
    public double? GainAbsolute => AfterFps is { } a && Before.Fps is { } b ? a - b : null;
    public ResultConfidence Confidence { get; set; }
    /// <summary>Why the gain is what it is (explanation keys, e.g. "noFpsTweaks", "withinNoise", "gpuBoundCpuGain").</summary>
    public List<string> ReasonKeys { get; } = new();
}

/// <summary>Machine data the estimator needs, extracted from the hardware profile (and the local benchmark).</summary>
public sealed class EstimatorMachine
{
    public string? GpuName { get; set; }
    public string? CpuName { get; set; }
    public double? GpuVramGb { get; set; }
    public bool GpuSupportsRayTracing { get; set; } = true;
    public double RamGb { get; set; }
    public ChannelMode Channels { get; set; }
    public XmpState Xmp { get; set; }

    public static EstimatorMachine From(MachineProfile p)
    {
        var g = p.PrimaryGpu;
        var name = g?.Name ?? "";
        return new EstimatorMachine
        {
            GpuName = g?.Name, CpuName = p.Cpu.Name,
            GpuVramGb = g?.DedicatedVramBytes is > 0 and var v && !(g?.IsIntegrated ?? false) ? v / 1073741824.0 : null,
            GpuSupportsRayTracing = !System.Text.RegularExpressions.Regex.IsMatch(name, @"GTX|RX 5\d{3}|RX [4-5]\d0\b|Vega|UHD|Iris|Radeon\(TM\) Graphics$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            RamGb = p.Memory.TotalBytes / 1073741824.0, Channels = p.Memory.Channels, Xmp = p.Memory.Xmp,
        };
    }
}

/// <summary>
/// FPS model (documented in docs/BENCHMARKS.md):
/// GPU-bound FPS = reference FPS × (GPU index / RTX 4070 index) × (pixels / 1440p pixels)^(−k) × preset factor;
/// CPU-bound FPS = CPU ceiling × (CPU index / 7800X3D index) × preset factor × memory factor;
/// FPS = soft minimum of both (p = 4), then VRAM shortfall and frame caps. The result is always a range whose width
/// reflects the quality of the data; a real measurement of the game on this machine replaces or calibrates it.
/// </summary>
public sealed class FpsEstimator
{
    private const double SoftMinPower = 4;
    private readonly GameLibrary _lib;
    private readonly IReadOnlyList<GameMeasurement> _measurements;

    public FpsEstimator(GameLibrary lib, IReadOnlyList<GameMeasurement>? measurements = null)
    {
        _lib = lib;
        _measurements = measurements ?? Array.Empty<GameMeasurement>();
    }

    private static double GpuPreset(GraphicsPreset p) => p switch { GraphicsPreset.Low => 1.75, GraphicsPreset.Medium => 1.35, GraphicsPreset.High => 1.0, _ => 0.8 };
    private static double CpuPreset(GraphicsPreset p) => p switch { GraphicsPreset.Low => 1.15, GraphicsPreset.Medium => 1.07, GraphicsPreset.High => 1.0, _ => 0.95 };
    private static double VramPreset(GraphicsPreset p) => p switch { GraphicsPreset.Low => 0.6, GraphicsPreset.Medium => 0.8, GraphicsPreset.High => 1.0, _ => 1.2 };

    public GameEstimate Estimate(GameInfo game, EstimatorMachine m, EstimateSettings s)
    {
        var e = new GameEstimate { Game = game, Settings = s };
        var exact = _measurements.Where(x => x.GameId == game.Id && x.Width == s.Width && x.Height == s.Height && x.Preset == s.Preset)
                                 .OrderByDescending(x => x.Utc).FirstOrDefault();
        var model = Model(game, m, s, e);
        if (exact != null)
        {
            e.Basis = EstimateBasis.Measured;
            e.Fps = Math.Round(exact.AvgFps);
            e.FpsLow = Math.Round(exact.AvgFps * 0.95);
            e.FpsHigh = Math.Round(exact.AvgFps * 1.05);
            e.Confidence = ResultConfidence.High;
            return e;
        }
        if (model == null) return e;

        var (fps, uncertainty) = model.Value;
        // Calibration: a real capture of this game on this machine, at other settings, rescales the model.
        var other = _measurements.Where(x => x.GameId == game.Id).OrderByDescending(x => x.Utc).FirstOrDefault();
        if (other != null)
        {
            var probe = new GameEstimate { Game = game, Settings = new EstimateSettings(other.Width, other.Height, other.Preset) };
            if (Model(game, m, probe.Settings, probe) is { } atMeasured && atMeasured.fps > 0)
            {
                var k = Math.Clamp(other.AvgFps / atMeasured.fps, 0.5, 2.0);
                fps *= k;
                uncertainty = Math.Min(uncertainty, 0.15);
                e.Basis = EstimateBasis.CalibratedByMeasurement;
                e.NoteKeys.Add("game.note.calibrated");
            }
        }
        if (game.FpsCap is { } cap && fps > cap) { fps = cap; e.Limiter = Limiter.FrameCap; e.NoteKeys.Add("game.note.cap"); uncertainty = Math.Min(uncertainty, 0.1); }
        e.Fps = Math.Round(fps);
        e.FpsLow = Math.Round(fps * (1 - uncertainty));
        e.FpsHigh = Math.Round(game.FpsCap is { } c2 ? Math.Min(c2, fps * (1 + uncertainty)) : fps * (1 + uncertainty));
        e.Confidence = e.Basis == EstimateBasis.CalibratedByMeasurement ? ResultConfidence.High
                     : uncertainty <= 0.2 ? ResultConfidence.Medium : ResultConfidence.Low;
        return e;
    }

    /// <summary>Returns (fps, relative uncertainty) or null when there is not enough data.</summary>
    private (double fps, double uncertainty)? Model(GameInfo game, EstimatorMachine m, EstimateSettings s, GameEstimate e)
    {
        var (gpu, gq) = _lib.MatchGpu(m.GpuName, m.CpuName);
        var (cpu, cq) = _lib.MatchCpu(m.CpuName);
        if (gpu == null)
        {
            e.Basis = EstimateBasis.Insufficient;
            e.Limiter = Limiter.Unknown;
            e.NoteKeys.Add("game.note.unknownGpu");
            return null;
        }
        if (game.RequiresRayTracing && !m.GpuSupportsRayTracing)
        {
            e.Basis = EstimateBasis.Insufficient;
            e.NoteKeys.Add("game.note.rtRequired");
            return null;
        }
        var uncertainty = 0.20;
        if (gq == MatchQuality.Approximate) uncertainty += 0.10;
        if (cpu == null) { uncertainty += 0.10; e.NoteKeys.Add("game.note.unknownCpu"); }
        uncertainty += game.DataQuality switch { 0 => 0.12, 2 => -0.05, _ => 0 };

        var pixels = (double)s.Width * s.Height / (2560.0 * 1440.0);
        var gpuFps = game.RefFps * (gpu.Index / GameLibrary.ReferenceGpuIndex) * Math.Pow(pixels, -game.ResolutionExponent) * GpuPreset(s.Preset);
        var cpuIndex = cpu?.Index ?? 60; // unknown CPU: mid-range assumption, flagged and widened above
        var memFactor = 1.0;
        if (m.Channels == ChannelMode.Single) { memFactor *= 1 - 0.30 * game.MemorySensitivity; e.NoteKeys.Add("game.note.singleChannel"); }
        if (m.Xmp == XmpState.LikelyDisabled) { memFactor *= 1 - 0.12 * game.MemorySensitivity; e.NoteKeys.Add("game.note.xmpOff"); }
        if (m.RamGb > 0 && m.RamGb + 0.5 < game.RamGb) { memFactor *= 0.85; uncertainty += 0.05; e.NoteKeys.Add("game.note.lowRam"); }
        var cpuFps = game.CpuCeilingFps * (cpuIndex / GameLibrary.ReferenceCpuIndex) * CpuPreset(s.Preset) * memFactor;

        var fps = Math.Pow(Math.Pow(gpuFps, -SoftMinPower) + Math.Pow(cpuFps, -SoftMinPower), -1 / SoftMinPower);
        e.GpuBoundFps = Math.Round(gpuFps);
        e.CpuBoundFps = Math.Round(cpuFps);
        e.GpuShare = Math.Pow(fps / gpuFps, SoftMinPower);
        e.CpuShare = Math.Pow(fps / cpuFps, SoftMinPower);
        e.Limiter = gpuFps < cpuFps * 0.85 ? Limiter.Gpu : cpuFps < gpuFps * 0.85 ? Limiter.Cpu : Limiter.Balanced;

        // VRAM: dedicated memory below what the settings need -> streaming from system memory.
        var need = game.VramGb1440High * Math.Pow(pixels, 0.35) * VramPreset(s.Preset);
        var vram = m.GpuVramGb ?? (gpu.VramGb > 0 ? gpu.VramGb : (double?)null);
        if (vram is { } v && v + 0.25 < need)
        {
            fps *= Math.Pow(Math.Max(0.55, v / need), 1.2);
            e.Limiter = Limiter.Vram;
            uncertainty += 0.08;
            e.NoteKeys.Add("game.note.vram");
        }
        if (gpu.VramGb == 0) e.NoteKeys.Add("game.note.igpu");
        e.Basis = gq == MatchQuality.Exact && cpu != null ? EstimateBasis.Theoretical : EstimateBasis.ExtrapolatedFromSimilar;
        return (fps, Math.Clamp(uncertainty, 0.1, 0.5));
    }

    /// <summary>
    /// Projects the measured benchmark change onto a game. Only statistically confirmed component changes count;
    /// changes that cannot affect frame rate (network, privacy…) never produce a gain.
    /// </summary>
    public static GameGain ProjectGain(GameEstimate before, QuickBenchComparison? cmp, IReadOnlyCollection<TweakDefinition> appliedTweaks)
    {
        var g = new GameGain { Before = before };
        if (before.Fps == null) { g.ReasonKeys.Add("game.gain.noEstimate"); return g; }
        // Nothing optimised and nothing measured: there is no "after" yet (never shown as "72 → 72, +0 %").
        if (cmp == null && appliedTweaks.Count == 0) { g.ReasonKeys.Add("game.gain.notOptimized"); return g; }
        var fpsTweaks = appliedTweaks.Any(t => t.CanAffectFrameRate);
        if (cmp == null)
        {
            if (fpsTweaks) { g.ReasonKeys.Add("game.gain.needBenchmark"); return g; }
            // Nothing applied can change frame rate (network, privacy, comfort…): no gain is claimed.
            g.GainPercent = 0; g.GainPercentLow = 0; g.GainPercentHigh = 0; g.AfterFps = before.Fps;
            g.Confidence = ResultConfidence.High;
            g.ReasonKeys.Add("game.gain.noFpsTweaks");
            return g;
        }
        if (!fpsTweaks) g.ReasonKeys.Add("game.gain.measuredWithoutFpsTweaks");

        double Change(params string[] keys)
        {
            var ms = cmp.Metrics.Where(m => keys.Contains(m.Key) && m.Verdict is ChangeVerdict.Improved or ChangeVerdict.Regressed).ToList();
            return ms.Count == 0 ? 0 : ms.Average(m => m.ImprovementPercent!.Value);
        }
        var cpu = 0.6 * Change("cpu.single") + 0.4 * Change("cpu.multi", "cpu.sustained");
        var mem = Change("mem.bandwidth", "mem.latency");
        var gpu = Change("gpu.compute");
        var gain = before.GpuShare * gpu + before.CpuShare * (0.7 * cpu + before.Game.MemorySensitivity * 0.5 * mem);
        if (before.Limiter == Limiter.FrameCap) { gain = 0; g.ReasonKeys.Add("game.gain.capped"); }
        else if (Math.Abs(gain) < 0.5) g.ReasonKeys.Add(cmp.Metrics.Any(m => m.Verdict is ChangeVerdict.Improved or ChangeVerdict.Regressed) ? "game.gain.notRelevant" : "game.gain.withinNoise");
        else g.ReasonKeys.Add(before.Limiter == Limiter.Gpu && Math.Abs(gpu) < 0.5 ? "game.gain.gpuBoundCpuGain" : "game.gain.fromBenchmark");

        g.GainPercent = Math.Round(gain, 1);
        g.GainPercentLow = Math.Round(gain * 0.5, 1);
        g.GainPercentHigh = Math.Round(gain * 1.5, 1);
        var after = before.Fps.Value * (1 + gain / 100);
        if (before.Game.FpsCap is { } cap) after = Math.Min(after, cap);
        g.AfterFps = Math.Round(after);
        g.Confidence = before.Confidence == ResultConfidence.Low ? ResultConfidence.Low : ResultConfidence.Medium;
        return g;
    }

    /// <summary>Highest preset that keeps about 60 FPS (competitive games: low settings, as players do).</summary>
    public GraphicsPreset SuggestPreset(GameInfo game, EstimatorMachine m, int w, int h)
    {
        if (game.Genre is "competitive" or "battleroyale" or "moba") return GraphicsPreset.Low;
        foreach (var p in new[] { GraphicsPreset.Ultra, GraphicsPreset.High, GraphicsPreset.Medium })
            if (Estimate(game, m, new EstimateSettings(w, h, p)).Fps is >= 60) return p;
        return GraphicsPreset.Low;
    }

    /// <summary>
    /// Three relevant showcase games: a CPU-sensitive competitive title, a demanding single-player game and a popular one,
    /// favouring games already measured on this machine and never picking a game without a usable estimate.
    /// </summary>
    public List<GameEstimate> PickShowcase(EstimatorMachine m, int w, int h)
    {
        var all = _lib.Games.Select(g => Estimate(g, m, new EstimateSettings(w, h, SuggestPreset(g, m, w, h))))
                            .Where(e => e.Fps != null && e.Basis != EstimateBasis.Insufficient)
                            .OrderBy(e => e.Basis is EstimateBasis.Measured or EstimateBasis.CalibratedByMeasurement ? 0 : 1)
                            .ThenBy(e => e.Game.Popularity).ThenByDescending(e => e.Game.DataQuality).ToList();
        var picked = new List<GameEstimate>();
        void Take(Func<GameEstimate, bool> pred)
        {
            var c = all.FirstOrDefault(e => !picked.Contains(e) && pred(e) && e.Game.DataQuality > 0) ?? all.FirstOrDefault(e => !picked.Contains(e) && pred(e));
            if (c != null) picked.Add(c);
        }
        Take(e => e.Game.Genre is "competitive" or "battleroyale" or "moba");
        Take(e => e.Game.Genre is "openworld" or "action" or "rpg" && e.Fps >= 30);
        Take(_ => true);
        return picked;
    }
}
