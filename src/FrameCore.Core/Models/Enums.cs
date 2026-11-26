namespace FrameCore.Core.Models;

/// <summary>Level of evidence behind an optimization (see docs/research/03-INVENTAIRE-OPTIMISATIONS.md).</summary>
public enum Confidence
{
    /// <summary>Official documentation or reproducible, understood benefit.</summary>
    Confirmed,
    /// <summary>Plausible benefit on some configurations, known mechanism.</summary>
    Probable,
    /// <summary>Variable results: never applied automatically, requires specific validation.</summary>
    Experimental,
    /// <summary>No established benefit, placebo, obsolete or too risky: never applied automatically.</summary>
    NotRecommended,
}

public enum RiskLevel { None, Low, Medium, High }

public enum RestartRequirement { None, SignOut, Reboot, ExplorerRestart, AdapterRestart }

/// <summary>Nature of the change, so comfort settings are never presented as FPS gains.</summary>
public enum TweakKind
{
    Performance,
    Latency,
    Responsiveness,
    Background,
    Comfort,
    Privacy,
    Repair,
    Maintenance,
    Stability,
    Security,
}

/// <summary>Simple-mode levels: Équilibré, Performances, Performance maximale.</summary>
public enum ProfileLevel { Balanced = 1, Performance = 2, Maximum = 3 }

/// <summary>How long a change lasts.</summary>
public enum Persistence
{
    /// <summary>Stored setting, survives reboots until restored.</summary>
    Persistent,
    /// <summary>Lost at the next reboot (e.g. live driver values).</summary>
    UntilReboot,
    /// <summary>One-shot maintenance operation (cleanup, retrim): nothing to keep or restore.</summary>
    OneShot,
}

/// <summary>What an optimisation can influence. Used to explain results honestly (a network tweak never claims FPS).</summary>
public enum ImpactArea { Fps, FrameTimes, InputLatency, NetworkLatency, Responsiveness, Boot, Privacy, DiskSpace, Stability, Comfort, Battery }

public enum TweakStatus { Unknown, NotApplied, Applied, Partial, Unsupported, Error }

public enum Severity { Info, Suggestion, Warning, Critical }

public enum StartupClassification { Essential, Recommended, Optional, Risky, Unknown }

public enum TriState { Unknown, No, Yes }

public enum ChangeSetStatus { Pending, Applied, Failed, RolledBack, Restored, PartiallyRestored }

public enum PlanSection { Standard, Consent, Experimental }

/// <summary>How a displayed hardware value was obtained (shown to the user next to the value).</summary>
public enum DataConfidence
{
    /// <summary>Measured by FrameCore on this machine (counters, timers, topology).</summary>
    Measured,
    /// <summary>Reported by the hardware, firmware or driver (CPUID, SMBIOS, DXGI, EDID…).</summary>
    Reported,
    /// <summary>Deduced from indirect evidence (naming conventions, module count…): probable, not certain.</summary>
    Inferred,
    /// <summary>Not accessible on this machine (missing rights, sensor, API or virtual hardware).</summary>
    Unavailable,
}
