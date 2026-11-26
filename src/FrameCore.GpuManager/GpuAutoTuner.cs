using FrameCore.Core.Localization;

namespace FrameCore.GpuManager;

public sealed class GpuStabilityResult
{
    public TimeSpan Duration { get; set; }
    public long Iterations { get; set; }
    public long ComputeErrors { get; set; }
    public long MemoryErrors { get; set; }
    public bool DeviceLost { get; set; }
    public bool DriverReset { get; set; }
    public double ComputeScore { get; set; }
    public double BandwidthGBs { get; set; }
    public double? MaxTemperatureC { get; set; }
    public string? Error { get; set; }
    public bool Passed => ComputeErrors == 0 && MemoryErrors == 0 && !DeviceLost && !DriverReset && Error == null;
}

public interface IGpuStabilityTester
{
    GpuStabilityResult Run(TimeSpan duration, CancellationToken ct);
}

/// <summary>Bridge to the elevated worker (or a simulation in tests).</summary>
public interface IGpuTuningChannel
{
    GpuTuningCapabilities Capabilities(int index);
    GpuTuningState Read(int index);
    void Apply(GpuTuningState state);
}

public sealed class AutoTuneOptions
{
    public int CoreStepMhz { get; set; } = 15;
    /// <summary>Conservative ceiling, further bounded by the range reported by the driver.</summary>
    public int CoreCapMhz { get; set; } = 150;
    public int MemStepMhz { get; set; } = 50;
    public int MemCapMhz { get; set; } = 500;
    public bool TuneMemory { get; set; } = true;
    public TimeSpan StepDuration { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan FinalValidation { get; set; } = TimeSpan.FromMinutes(3);
    public double MaxTemperatureC { get; set; } = 83;
    /// <summary>A memory step that lowers the bandwidth score by more than this ratio is rejected (GDDR error correction retries).</summary>
    public double BandwidthRegressionTolerance { get; set; } = 0.015;
}

public sealed record AutoTuneStep(string Domain, int OffsetMhz, bool Passed, string Detail);

public sealed class AutoTuneResult
{
    public bool Success { get; set; }
    public int BaselineCore { get; set; }
    public int BaselineMem { get; set; }
    public int FinalCore { get; set; }
    public int FinalMem { get; set; }
    public string Message { get; set; } = "";
    public List<AutoTuneStep> Steps { get; } = new();
    public bool Cancelled { get; set; }
}

/// <summary>
/// Conservative step search for NVIDIA clock offsets (no voltage change). Each step is validated by a
/// verified GPU workload; the first failure returns to the last stable step minus one step of margin,
/// and the result is only kept after a longer final validation. Any failure there restores the baseline.
/// </summary>
public sealed class GpuAutoTuner
{
    private readonly IGpuTuningChannel _channel;
    private readonly IGpuStabilityTester _tester;
    public GpuAutoTuner(IGpuTuningChannel channel, IGpuStabilityTester tester) { _channel = channel; _tester = tester; }

    public AutoTuneResult Run(int index, AutoTuneOptions o, IProgress<string>? progress, CancellationToken ct)
    {
        var caps = _channel.Capabilities(index);
        var result = new AutoTuneResult();
        if (!caps.CoreOffset) { result.Message = Loc.T("autotune.unsupported"); return result; }
        var baseline = _channel.Read(index);
        result.BaselineCore = baseline.CoreOffsetMhz ?? 0;
        result.BaselineMem = baseline.MemOffsetMhz ?? 0;

        bool Ok(GpuStabilityResult r) => r.Passed && (r.MaxTemperatureC == null || r.MaxTemperatureC < o.MaxTemperatureC);
        string Describe(GpuStabilityResult r) => r.Error ?? Loc.T("autotune.stepDetail", r.ComputeErrors + r.MemoryErrors, r.MaxTemperatureC?.ToString("0") ?? "?", r.ComputeScore.ToString("0"));

        void Restore()
        {
            try { _channel.Apply(new GpuTuningState { Index = index, CoreOffsetMhz = result.BaselineCore, MemOffsetMhz = caps.MemOffset ? result.BaselineMem : null }); }
            catch { /* best effort; offsets are also reset by a reboot */ }
        }

        progress?.Report(Loc.T("autotune.baseline"));
        var first = _tester.Run(o.StepDuration, ct);
        result.Steps.Add(new AutoTuneStep("baseline", result.BaselineCore, Ok(first), Describe(first)));
        if (ct.IsCancellationRequested) { result.Cancelled = true; return result; }
        if (!Ok(first)) { result.Message = Loc.T("autotune.baselineUnstable"); return result; }

        try
        {
            // 1. Core clock
            var bestCore = result.BaselineCore;
            var ceiling = Math.Min(caps.CoreMax, result.BaselineCore + o.CoreCapMhz);
            var coreFailed = false;
            for (var off = bestCore + o.CoreStepMhz; off <= ceiling && !ct.IsCancellationRequested; off += o.CoreStepMhz)
            {
                progress?.Report(Loc.T("autotune.testingCore", off));
                _channel.Apply(new GpuTuningState { Index = index, CoreOffsetMhz = off });
                var r = _tester.Run(o.StepDuration, ct);
                var pass = Ok(r);
                result.Steps.Add(new AutoTuneStep("core", off, pass, Describe(r)));
                if (!pass) { coreFailed = true; break; }
                bestCore = off;
            }
            if (coreFailed) bestCore = Math.Max(result.BaselineCore, bestCore - o.CoreStepMhz);
            _channel.Apply(new GpuTuningState { Index = index, CoreOffsetMhz = bestCore });

            // 2. Memory clock (bandwidth must keep improving)
            var bestMem = result.BaselineMem;
            if (o.TuneMemory && caps.MemOffset && !ct.IsCancellationRequested)
            {
                var reference = _tester.Run(o.StepDuration, ct);
                var bestBandwidth = reference.BandwidthGBs;
                var memCeiling = Math.Min(caps.MemMax, result.BaselineMem + o.MemCapMhz);
                var memFailed = false;
                for (var off = bestMem + o.MemStepMhz; off <= memCeiling && !ct.IsCancellationRequested; off += o.MemStepMhz)
                {
                    progress?.Report(Loc.T("autotune.testingMem", off));
                    _channel.Apply(new GpuTuningState { Index = index, MemOffsetMhz = off });
                    var r = _tester.Run(o.StepDuration, ct);
                    var regressed = bestBandwidth > 0 && r.BandwidthGBs < bestBandwidth * (1 - o.BandwidthRegressionTolerance);
                    var pass = Ok(r) && !regressed;
                    result.Steps.Add(new AutoTuneStep("memory", off, pass, regressed ? Loc.T("autotune.bandwidthRegressed", r.BandwidthGBs.ToString("0.0"), bestBandwidth.ToString("0.0")) : Describe(r)));
                    if (!pass) { memFailed = true; break; }
                    bestMem = off;
                    bestBandwidth = Math.Max(bestBandwidth, r.BandwidthGBs);
                }
                if (memFailed) bestMem = Math.Max(result.BaselineMem, bestMem - o.MemStepMhz);
                _channel.Apply(new GpuTuningState { Index = index, MemOffsetMhz = bestMem });
            }

            if (ct.IsCancellationRequested) { Restore(); result.Cancelled = true; result.Message = Loc.T("autotune.cancelled"); return result; }

            // 3. Final, longer validation of the combination.
            progress?.Report(Loc.T("autotune.final"));
            var final = _tester.Run(o.FinalValidation, ct);
            result.Steps.Add(new AutoTuneStep("final", bestCore, Ok(final), Describe(final)));
            if (!Ok(final))
            {
                Restore();
                result.Message = Loc.T("autotune.finalFailed");
                return result;
            }
            result.FinalCore = bestCore;
            result.FinalMem = bestMem;
            result.Success = true;
            result.Message = Loc.T("autotune.success", bestCore - result.BaselineCore, bestMem - result.BaselineMem);
            return result;
        }
        catch (Exception ex)
        {
            Restore();
            result.Message = Loc.T("autotune.error", ex.Message);
            return result;
        }
    }
}
