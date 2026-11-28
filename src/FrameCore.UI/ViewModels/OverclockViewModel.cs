using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.CpuManager;
using FrameCore.GpuManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed class GpuProfile
{
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string GpuName { get; set; } = "";
    public int Index { get; set; }
    public int? CoreOffsetMhz { get; set; }
    public int? MemOffsetMhz { get; set; }
    public double? PowerLimit { get; set; }
    public DateTime ValidatedAt { get; set; } = DateTime.Now;
    public string Validation { get; set; } = "";
    public string Display => $"{Name} — {GpuName}: core {CoreOffsetMhz:+0;-0;0} MHz, mem {MemOffsetMhz:+0;-0;0} MHz" + (PowerLimit != null ? $", power {PowerLimit:0}" : "") + $" ({ValidatedAt:g})";
}

/// <summary>Bridges the auto-tuner to the elevated worker (writes) and to local NVML reads.</summary>
public sealed class PrivilegedGpuChannel : IGpuTuningChannel
{
    private readonly PrivilegedClient _p;
    private readonly GpuTuningCapabilities _caps;
    public PrivilegedGpuChannel(PrivilegedClient p, GpuTuningCapabilities caps) { _p = p; _caps = caps; }
    public GpuTuningCapabilities Capabilities(int index) => _caps;
    public GpuTuningState Read(int index) => _p.GpuRead(_caps.Vendor, index);
    public void Apply(GpuTuningState state) => _p.GpuApply(_caps.Vendor, state);
}

public sealed partial class OverclockViewModel : ObservableObject, IDiagnosticConsumer
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _cts;

    // CPU
    [ObservableProperty] private CpuTuningReport? _cpuReport;
    [ObservableProperty] private string _cpuSupport = "";
    [ObservableProperty] private int _stressMinutes = 5;
    [ObservableProperty] private bool _stressRunning;
    [ObservableProperty] private string _stressStatus = "";
    [ObservableProperty] private double _stressProgress;
    [ObservableProperty] private string _stressResult = "";

    // GPU
    [ObservableProperty] private GpuTuningCapabilities? _selectedGpu;
    [ObservableProperty] private string _gpuStatus = "";
    [ObservableProperty] private bool _gpuLoaded;
    [ObservableProperty] private double _coreOffset;
    [ObservableProperty] private double _memOffset;
    [ObservableProperty] private double _powerLimit;
    [ObservableProperty] private string _currentState = "";
    [ObservableProperty] private bool _gpuBusy;
    [ObservableProperty] private int _autoCoreCap = 150;
    [ObservableProperty] private bool _autoTuneMemory = true;
    [ObservableProperty] private int _autoStepSeconds = 60;
    [ObservableProperty] private string _gpuTestResult = "";
    [ObservableProperty] private bool _acknowledgeRisk;
    /// <summary>Light | Moderate | High | Custom (automatic GPU tuning level).</summary>
    [ObservableProperty] private string _ocLevel = "Moderate";
    public bool IsCustomLevel => OcLevel == "Custom";
    public bool IsAmd => SelectedGpu?.Vendor == "AMD";

    /// <summary>What the chosen level does on this card, with its concrete values.</summary>
    public string OcLevelText
    {
        get
        {
            if (SelectedGpu is not { Available: true } caps) return "";
            var level = Enum.TryParse<GpuOcLevel>(OcLevel, out var l) ? l : GpuOcLevel.Moderate;
            if (level == GpuOcLevel.Custom) return Loc.T("oc.level.custom.desc");
            if (caps.Vendor == "AMD")
            {
                var (preset, maxPower) = GpuOcLevels.AmdPlan(level);
                return Loc.T("oc.level.amd." + level) + (maxPower && caps.PowerLimit ? " " + Loc.T("oc.level.powerMax", caps.PowerMax.ToString("0"), caps.PowerUnit) : "");
            }
            var o = GpuOcLevels.For(level);
            return Loc.T("oc.level.nvidia", Math.Min(o.CoreCapMhz, caps.CoreMax), caps.MemOffset ? Math.Min(o.MemCapMhz, caps.MemMax) : 0, o.StepDuration.TotalSeconds,
                       o.FinalValidation.TotalMinutes, o.MaxTemperatureC, o.MarginSteps)
                 + " " + (o.RaisePowerLimit && caps.PowerLimit ? Loc.T("oc.level.powerMax", caps.PowerMax.ToString("0"), caps.PowerUnit) : Loc.T("oc.level.powerKept"))
                 + " " + Loc.T("oc.level.nvidiaVoltage");
        }
    }

    partial void OnOcLevelChanged(string value) { OnPropertyChanged(nameof(IsCustomLevel)); OnPropertyChanged(nameof(OcLevelText)); }

    /// <summary>Runs the chosen level: NVIDIA = verified step search; AMD = the driver's automatic tuning (+ power limit in High).</summary>
    [RelayCommand]
    private async Task StartLevel()
    {
        if (SelectedGpu is not { Available: true } caps || !ConfirmRisk()) return;
        var level = Enum.TryParse<GpuOcLevel>(OcLevel, out var l) ? l : GpuOcLevel.Moderate;
        if (level == GpuOcLevel.Custom) { await StartAutoTune(); return; }
        if (caps.Vendor == "AMD")
        {
            var (preset, maxPower) = GpuOcLevels.AmdPlan(level);
            if (!Dialogs.Confirm(Loc.T("oc.level.title", Loc.T("oc.level." + level)), OcLevelText, Loc.T("oc.auto.start"), true)) return;
            await RunPresetCore(caps, preset, maxPower && caps.PowerLimit ? caps.PowerMax : null, Loc.T("oc.level." + level));
            return;
        }
        if (!caps.CoreOffset) return;
        if (!Dialogs.Confirm(Loc.T("oc.level.title", Loc.T("oc.level." + level)), OcLevelText, Loc.T("oc.auto.start"), true)) return;
        await RunAutoTune(caps, GpuOcLevels.For(level), Loc.T("oc.level." + level));
    }

    public ObservableCollection<GpuTuningCapabilities> GpuCaps { get; } = new();
    public ObservableCollection<string> AutoTuneLog { get; } = new();
    public ObservableCollection<GpuProfile> Profiles { get; } = new();

    public CpuGuideViewModel CpuGuide { get; }

    public OverclockViewModel(ShellViewModel shell)
    {
        _shell = shell;
        CpuGuide = new CpuGuideViewModel(shell.Services.CpuOcGuide);
        LoadProfiles();
    }

    public void OnDiagnostic(DiagnosticResult r)
    {
        CpuReport = r.CpuTuning;
        CpuSupport = r.CpuTuning == null ? "" : Loc.T("oc.cpu.support." + r.CpuTuning.OverclockSupported);
        CpuGuide.OnProfile(r.Profile);
    }

    // ------------------------------------------------------------------ CPU
    [RelayCommand]
    private async Task StartStress()
    {
        if (StressRunning) return;
        StressRunning = true;
        StressResult = "";
        _cts = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        var duration = TimeSpan.FromMinutes(StressMinutes);
        var progress = new Progress<StressProgress>(p =>
        {
            StressProgress = Math.Min(100, p.Elapsed.TotalSeconds / duration.TotalSeconds * 100);
            var m = _shell.Monitoring.Last;
            StressStatus = Loc.T("oc.stress.status", p.Elapsed.ToString(@"mm\:ss"), p.IterationsPerSecond.ToString("0"), p.Errors,
                m?.CpuTemp?.ToString("0") ?? Loc.Unknown, m?.Cpu.EffectiveMhz?.ToString("0") ?? "–", m?.Cpu.PerformanceLimitPercent?.ToString("0") ?? "–");
        });
        try
        {
            var r = await Task.Run(() => CpuStressTest.Run(duration, Environment.ProcessorCount, progress, _cts.Token));
            var whea = await Task.Run(() => EventLogQuery.Since("System", "Microsoft-Windows-WHEA-Logger", started));
            var lines = new List<string>
            {
                r.Cancelled ? Loc.T("oc.stress.cancelled") : r.Errors == 0 ? Loc.T("oc.stress.pass") : Loc.T("oc.stress.fail", r.Errors),
                Loc.T("oc.stress.score", r.Score.ToString("0"), r.Threads, r.Duration.ToString(@"mm\:ss")),
                whea.Count == 0 ? Loc.T("oc.stress.noWhea") : Loc.T("oc.stress.whea", whea.Count),
                Loc.T("oc.stress.disclaimer"),
            };
            StressResult = string.Join("\n", lines);
        }
        finally { StressRunning = false; StressProgress = 0; }
    }

    [RelayCommand] private void CancelStress() => _cts?.Cancel();
    [RelayCommand] private void OpenUri(string uri) => Dialogs.OpenUri(uri);

    // ------------------------------------------------------------------ GPU
    [RelayCommand]
    private async Task LoadGpu()
    {
        GpuBusy = true;
        GpuStatus = Loc.T("oc.gpu.loading");
        try
        {
            // Reads work without administrator rights (NVML/ADLX); writes go through the elevated worker.
            var caps = await Task.Run(() => { using var svc = new GpuTuningService(); return svc.Capabilities().ToList(); });
            GpuCaps.Clear();
            foreach (var c in caps) GpuCaps.Add(c);
            var unsupported = _shell.Diagnostic?.Profile.Gpus.Where(g => !g.IsSoftware && !g.IsIntegrated && g.Vendor is not (GpuVendor.Nvidia or GpuVendor.Amd)).ToList() ?? new();
            foreach (var g in unsupported)
                GpuCaps.Add(new GpuTuningCapabilities
                {
                    GpuName = g.Name, Vendor = g.Vendor.ToString(), Available = false, UnavailableReason = Loc.T("oc.gpu.intelUnavailable"),
                    OfficialTool = "Intel Graphics Software", OfficialToolUrl = "https://www.intel.com/content/www/us/en/products/docs/discrete-gpus/arc/software/graphics-software.html",
                });
            SelectedGpu = GpuCaps.FirstOrDefault(c => c.Available) ?? GpuCaps.FirstOrDefault();
            GpuLoaded = true;
            GpuStatus = GpuCaps.Count == 0 ? Loc.T("oc.gpu.none") : "";
        }
        catch (Exception ex) { GpuStatus = ex.Message; }
        finally { GpuBusy = false; }
    }

    partial void OnSelectedGpuChanged(GpuTuningCapabilities? value)
    {
        OnPropertyChanged(nameof(OcLevelText)); OnPropertyChanged(nameof(IsAmd));
        CoreOffset = 0; MemOffset = 0; PowerLimit = value?.PowerDefault ?? 0;
        CurrentState = "";
        if (value is { Available: true })
        {
            try
            {
                using var svc = new GpuTuningService();
                var st = svc.Read(value.Vendor, value.Index);
                CoreOffset = st.CoreOffsetMhz ?? 0; MemOffset = st.MemOffsetMhz ?? 0; PowerLimit = st.PowerLimit ?? value.PowerDefault ?? 0;
                CurrentState = Loc.T("oc.gpu.current", st.CoreOffsetMhz?.ToString("+0;-0;0") ?? "–", st.MemOffsetMhz?.ToString("+0;-0;0") ?? "–",
                    st.PowerLimit?.ToString("0") ?? "–", value.PowerUnit, st.ActivePreset ?? "–");
            }
            catch (Exception ex) { CurrentState = ex.Message; }
        }
    }

    private bool ConfirmRisk()
    {
        if (!AcknowledgeRisk) { Dialogs.Info(Loc.T("oc.risk.title"), Loc.T("oc.risk.needAck")); return false; }
        return true;
    }

    [RelayCommand]
    private async Task ApplyManual()
    {
        if (SelectedGpu is not { Available: true } caps || !ConfirmRisk()) return;
        var state = new GpuTuningState
        {
            Index = caps.Index,
            CoreOffsetMhz = caps.CoreOffset ? (int)Math.Round(CoreOffset) : null,
            MemOffsetMhz = caps.MemOffset ? (int)Math.Round(MemOffset) : null,
            PowerLimit = caps.PowerLimit ? Math.Round(PowerLimit) : null,
        };
        if (!Dialogs.Confirm(Loc.T("oc.apply.title"), Loc.T("oc.apply.confirm", state.CoreOffsetMhz, state.MemOffsetMhz, state.PowerLimit, caps.PowerUnit), Loc.T("common.apply"), true)) return;
        await RunGpu(() => _shell.Privileged.GpuApply(caps.Vendor, state), Loc.T("oc.apply.done"));
        OnSelectedGpuChanged(caps);
    }

    [RelayCommand]
    private async Task ResetGpu()
    {
        if (SelectedGpu is not { Available: true } caps) return;
        await RunGpu(() => _shell.Privileged.GpuReset(caps.Vendor, caps.Index), Loc.T("oc.reset.done"));
        OnSelectedGpuChanged(caps);
    }

    [RelayCommand]
    private async Task RunPreset(string preset)
    {
        if (SelectedGpu is not { Available: true } caps || !ConfirmRisk()) return;
        if (!Dialogs.Confirm(Loc.T("oc.preset.title"), Loc.T("oc.preset.confirm", Loc.T("oc.preset." + preset)), Loc.T("common.apply"), true)) return;
        await RunPresetCore(caps, preset, null, Loc.T("oc.preset." + preset));
    }

    private async Task RunPresetCore(GpuTuningCapabilities caps, string preset, double? powerLimit, string profileName)
    {
        GpuBusy = true;
        AutoTuneLog.Clear();
        try
        {
            AutoTuneLog.Add(Loc.T("oc.preset.running"));
            var ok = await Task.Run(() => _shell.Privileged.GpuPreset(caps.Vendor, new GpuPresetRequest { Index = caps.Index, Preset = preset }));
            AutoTuneLog.Add(ok ? Loc.T("oc.preset.completed") : Loc.T("oc.preset.notCompleted"));
            if (powerLimit is { } pl)
            {
                await Task.Run(() => _shell.Privileged.GpuApply(caps.Vendor, new GpuTuningState { Index = caps.Index, PowerLimit = pl }));
                AutoTuneLog.Add(Loc.T("autotune.power", pl.ToString("0"), caps.PowerUnit));
            }
            AutoTuneLog.Add(Loc.T("oc.preset.validating"));
            var test = await Task.Run(() => Tester(caps).Run(TimeSpan.FromMinutes(3), CancellationToken.None));
            if (test.Passed)
            {
                AutoTuneLog.Add(Loc.T("oc.test.pass", test.ComputeScore.ToString("0"), test.BandwidthGBs.ToString("0.0")));
                SaveProfile(new GpuProfile { Name = profileName, Vendor = caps.Vendor, GpuName = caps.GpuName, Index = caps.Index, PowerLimit = powerLimit, Validation = "3 min" });
            }
            else
            {
                AutoTuneLog.Add(Loc.T("oc.test.fail", test.Error ?? (test.ComputeErrors + test.MemoryErrors).ToString()));
                await Task.Run(() => _shell.Privileged.GpuReset(caps.Vendor, caps.Index));
                AutoTuneLog.Add(Loc.T("oc.reset.done"));
            }
        }
        catch (ElevationDeclinedException) { AutoTuneLog.Add(Loc.T("elevation.declined")); }
        catch (Exception ex) { AutoTuneLog.Add(ex.Message); }
        finally { GpuBusy = false; OnSelectedGpuChanged(caps); }
    }

    private D3D11StabilityTester Tester(GpuTuningCapabilities caps)
    {
        var info = _shell.Diagnostic?.Profile.Gpus.FirstOrDefault(g => GpuMonitor.NamesMatch(g.Name, caps.GpuName));
        return new D3D11StabilityTester(info?.Luid, () => _shell.Monitoring.Last?.PrimaryGpu?.TemperatureC ?? _shell.Monitoring.Last?.GpuTemp);
    }

    [RelayCommand]
    private async Task StartAutoTune()
    {
        if (SelectedGpu is not { Available: true, CoreOffset: true } caps || !ConfirmRisk()) return;
        if (!Dialogs.Confirm(Loc.T("oc.auto.title"), Loc.T("oc.auto.confirm", AutoCoreCap, AutoStepSeconds), Loc.T("oc.auto.start"), true)) return;
        await RunAutoTune(caps, new AutoTuneOptions { CoreCapMhz = AutoCoreCap, TuneMemory = AutoTuneMemory, StepDuration = TimeSpan.FromSeconds(AutoStepSeconds) }, Loc.T("oc.auto.profileName"));
    }

    private async Task RunAutoTune(GpuTuningCapabilities caps, AutoTuneOptions options, string profileName)
    {
        try { await Task.Run(() => _shell.Privileged.Ensure()); }
        catch (ElevationDeclinedException) { Dialogs.Info(Loc.T("oc.auto.title"), Loc.T("elevation.declined")); return; }
        GpuBusy = true;
        AutoTuneLog.Clear();
        _cts = new CancellationTokenSource();
        var tuner = new GpuAutoTuner(new PrivilegedGpuChannel(_shell.Privileged, caps), Tester(caps));
        var progress = new Progress<string>(s => AutoTuneLog.Add($"{DateTime.Now:T}  {s}"));
        try
        {
            var r = await Task.Run(() => tuner.Run(caps.Index, options, progress, _cts.Token));
            foreach (var s in r.Steps) AutoTuneLog.Add($"  {(s.Passed ? "✓" : "✗")} {s.Domain} {s.OffsetMhz:+0;-0;0} MHz — {s.Detail}");
            AutoTuneLog.Add(r.Message);
            if (r.Success)
                SaveProfile(new GpuProfile
                {
                    Name = profileName, Vendor = caps.Vendor, GpuName = caps.GpuName, Index = caps.Index,
                    CoreOffsetMhz = r.FinalCore, MemOffsetMhz = r.FinalMem, PowerLimit = r.FinalPower, Validation = options.FinalValidation.ToString(),
                });
        }
        catch (Exception ex) { AutoTuneLog.Add(ex.Message); }
        finally { GpuBusy = false; OnSelectedGpuChanged(caps); }
    }

    [RelayCommand] private void CancelAutoTune() => _cts?.Cancel();

    /// <summary>
    /// GPU tuning matched to an optimisation level (complete mode, the user ticked it): Performance → light,
    /// Maximum → moderate, Extreme → high. Same verified procedure and way back as the buttons of this page.
    /// </summary>
    public async Task<string> AutoTuneForLevelAsync(Core.Models.ProfileLevel level)
    {
        GpuOcLevel? gl = level switch
        {
            Core.Models.ProfileLevel.Performance => GpuOcLevel.Light, Core.Models.ProfileLevel.Maximum => GpuOcLevel.Moderate,
            Core.Models.ProfileLevel.Extreme => GpuOcLevel.High, _ => null,
        };
        if (gl is not { } lvl) return Loc.T("opt.complete.gpu.skip");
        if (!GpuLoaded) await LoadGpu();
        if (SelectedGpu is not { Available: true } caps) return Loc.T("opt.complete.gpu.unavailable");
        var name = Loc.T("oc.level." + lvl);
        if (caps.Vendor == "AMD")
        {
            var (preset, maxPower) = GpuOcLevels.AmdPlan(lvl);
            await RunPresetCore(caps, preset, maxPower && caps.PowerLimit ? caps.PowerMax : null, name);
        }
        else if (caps.CoreOffset) await RunAutoTune(caps, GpuOcLevels.For(lvl), name);
        else return Loc.T("opt.complete.gpu.unavailable");
        return Loc.T("opt.complete.gpu.done", name) + " " + (AutoTuneLog.LastOrDefault() ?? "");
    }

    [RelayCommand]
    private async Task RunGpuTest()
    {
        if (SelectedGpu is not { } caps) return;
        GpuBusy = true;
        GpuTestResult = Loc.T("oc.test.running");
        try
        {
            var r = await Task.Run(() => Tester(caps).Run(TimeSpan.FromMinutes(2), CancellationToken.None));
            GpuTestResult = r.Passed
                ? Loc.T("oc.test.pass", r.ComputeScore.ToString("0"), r.BandwidthGBs.ToString("0.0")) + "\n" + Loc.T("oc.stress.disclaimer")
                : Loc.T("oc.test.fail", r.Error ?? (r.ComputeErrors + r.MemoryErrors).ToString());
        }
        finally { GpuBusy = false; }
    }

    private async Task RunGpu(Func<bool> op, string success)
    {
        GpuBusy = true;
        try { await Task.Run(op); GpuStatus = success; }
        catch (ElevationDeclinedException) { GpuStatus = Loc.T("elevation.declined"); }
        catch (Exception ex) { GpuStatus = ex.Message; }
        finally { GpuBusy = false; }
    }

    private string ProfilesFile => Path.Combine(AppPaths.Profiles, "gpu-profiles.json");

    private void LoadProfiles()
    {
        try { if (File.Exists(ProfilesFile)) foreach (var p in Json.Read<List<GpuProfile>>(ProfilesFile) ?? new()) Profiles.Add(p); }
        catch (Exception ex) { Log.Warn("Profiles", ex.Message); }
    }

    private void SaveProfile(GpuProfile p)
    {
        Profiles.Insert(0, p);
        Json.WriteAtomic(ProfilesFile, Profiles.ToList());
    }

    [RelayCommand]
    private async Task ApplyProfile(GpuProfile? p)
    {
        if (p == null || !ConfirmRisk()) return;
        if (p.CoreOffsetMhz == null && p.MemOffsetMhz == null)
        {
            await RunGpu(() => _shell.Privileged.GpuPreset(p.Vendor, new GpuPresetRequest { Index = p.Index, Preset = PresetOf(p.Name) }), Loc.T("oc.apply.done"));
            return;
        }
        await RunGpu(() => _shell.Privileged.GpuApply(p.Vendor, new GpuTuningState { Index = p.Index, CoreOffsetMhz = p.CoreOffsetMhz, MemOffsetMhz = p.MemOffsetMhz, PowerLimit = p.PowerLimit }), Loc.T("oc.apply.done"));
    }

    private static string PresetOf(string name) =>
        new[] { "undervoltGpu", "overclockGpu", "overclockVram" }.FirstOrDefault(k => Loc.T("oc.preset." + k) == name) ?? "undervoltGpu";

    [RelayCommand]
    private void DeleteProfile(GpuProfile? p)
    {
        if (p == null) return;
        Profiles.Remove(p);
        Json.WriteAtomic(ProfilesFile, Profiles.ToList());
    }
}
