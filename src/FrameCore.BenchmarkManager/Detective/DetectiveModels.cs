using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>Areas of the full investigation ("detective").</summary>
public enum DetectiveArea { Degradation, Latency, Network, Stutter, Stability, Hardware, Processes, Storage }

/// <summary>Six tiers, from harmful to essential, used to decide what each optimisation level may switch off.</summary>
public enum ProcessTier { Harmful, Useless, LowValue, Useful, VeryUseful, Essential }

/// <summary>What FrameCore can do by itself about a finding (null = advice only).</summary>
public enum DetectiveFix { None, DisableStartup, ServiceManual, CloseProcess, LowerPriority, GameProfile, CleanShaderCache, OpenMemoryTest, OpenPage }

/// <summary>One fact found by the investigation, with its evidence (numbers) and what to do.</summary>
public sealed class DetectiveFinding
{
    public string Id { get; set; } = "";
    public DetectiveArea Area { get; set; }
    public Severity Severity { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The measured facts that support the finding.</summary>
    public string Evidence { get; set; } = "";
    /// <summary>What to do (always shown, even when FrameCore can fix it).</summary>
    public string Advice { get; set; } = "";
    public DetectiveFix Fix { get; set; }
    /// <summary>Target of the fix (process name, service, page key…).</summary>
    public string? FixTarget { get; set; }
    public bool AutoFixable => Fix is not (DetectiveFix.None or DetectiveFix.OpenPage or DetectiveFix.OpenMemoryTest);
}

/// <summary>Per-driver latency (DPC + ISR), as a LatencyMon-style summary.</summary>
public sealed class DriverLatency
{
    public string Driver { get; set; } = "";
    /// <summary>network | graphics | storage | audio | usb | power | kernel | other</summary>
    public string Component { get; set; } = "other";
    public long Count { get; set; }
    public double MaxMs { get; set; }
    public double P99Ms { get; set; }
    public double TotalMs { get; set; }
}

public sealed class LatencyReport
{
    public double Seconds { get; set; }
    public long DpcCount { get; set; }
    public long IsrCount { get; set; }
    public double MaxDpcMs { get; set; }
    public double MaxIsrMs { get; set; }
    /// <summary>Share of one CPU spent in DPC/ISR (all cores summed), in percent.</summary>
    public double BusyPercent { get; set; }
    public List<DriverLatency> Drivers { get; set; } = new();
    public string? Error { get; set; }
    public bool Measured => Error == null && Seconds > 0;
}

public sealed class NetworkReport
{
    public string? Adapter { get; set; }
    public bool Wired { get; set; }
    public long LinkMbps { get; set; }
    public PingSummary? Gateway { get; set; }
    public PingSummary? Internet { get; set; }
    public PingSummary? GatewayLoaded { get; set; }
    public PingSummary? InternetLoaded { get; set; }
    public double BackgroundKBps { get; set; }
    public List<string> VirtualAdapters { get; set; } = new();
}

public sealed class PingSummary
{
    public string Target { get; set; } = "";
    public int Sent { get; set; }
    public int Received { get; set; }
    public double AvgMs { get; set; }
    public double JitterMs { get; set; }
    public double MaxMs { get; set; }
    public double P95Ms { get; set; }
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
    public List<double> Samples { get; set; } = new();
}

/// <summary>One crash/instability event read from Windows' logs.</summary>
public sealed class StabilityEvent
{
    public DateTime Time { get; set; }
    /// <summary>bugcheck | unexpectedShutdown | whea | gpuReset | appCrash | diskError | memoryTest</summary>
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Code { get; set; }
}

public sealed class ProcessVerdict
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public int Instances { get; set; }
    public List<int> Pids { get; set; } = new();
    public ProcessTier Tier { get; set; }
    public string Category { get; set; } = "";
    public string Reason { get; set; } = "";
    public double CpuPercent { get; set; }
    public double WakeupsPerSec { get; set; }
    public double MemoryMb { get; set; }
    public bool HasWindow { get; set; }
    public bool Protected { get; set; }
    public List<string> Services { get; set; } = new();
    public bool IsGame { get; set; }
}

public sealed class DetectiveReport
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime FinishedUtc { get; set; }
    public string MachineSignature { get; set; } = "";
    public List<DetectiveFinding> Findings { get; set; } = new();
    public LatencyReport? Latency { get; set; }
    public NetworkReport? Network { get; set; }
    public List<StabilityEvent> Stability { get; set; } = new();
    public List<ProcessVerdict> Processes { get; set; } = new();
    /// <summary>Snapshot used later to explain a slowdown ("better when it was new").</summary>
    public SystemSnapshot Snapshot { get; set; } = new();
    public int Count(Severity s) => Findings.Count(f => f.Severity == s);
}

/// <summary>Facts kept from each investigation to compare a later one with (the first one becomes the baseline).</summary>
public sealed class SystemSnapshot
{
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public int? RamSpeedMts { get; set; }
    public int StartupEnabled { get; set; }
    public int ProcessCount { get; set; }
    public double BackgroundCpuPercent { get; set; }
    public double WakeupsPerSec { get; set; }
    public double SystemFreePercent { get; set; }
    public string? GpuDriver { get; set; }
    public int OsBuild { get; set; }
    public double? MaxDpcMs { get; set; }
    public double? GatewayPingMs { get; set; }
    public double? CpuIdleTempC { get; set; }
    public List<string> StartupNames { get; set; } = new();
}
