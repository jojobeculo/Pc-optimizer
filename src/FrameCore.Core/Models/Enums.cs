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

public enum ProfileLevel { Light = 1, Balanced = 2, Maximum = 3 }

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
