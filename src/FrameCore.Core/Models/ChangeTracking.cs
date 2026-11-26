namespace FrameCore.Core.Models;

/// <summary>
/// The real state captured before a change. Restoration always uses this value, never an assumed default.
/// </summary>
public sealed class StateSnapshot
{
    /// <summary>False when the registry value / element / setting did not exist (restore = delete).</summary>
    public bool Existed { get; set; }
    public string? Kind { get; set; }
    public string? Value { get; set; }
    public Dictionary<string, string>? Extra { get; set; }

    public static StateSnapshot Absent() => new() { Existed = false };
    public static StateSnapshot Of(string? value, string? kind = null) => new() { Existed = true, Value = value, Kind = kind };

    public string Describe() => Existed ? (Value ?? "") : "(absent)";
}

/// <summary>Live state of one action compared to its target.</summary>
public sealed class ActionState
{
    public bool Readable { get; set; } = true;
    /// <summary>The target does not exist on this machine (e.g. processor power settings inside a VM).</summary>
    public bool NotApplicable { get; set; }
    public bool Matches { get; set; }
    public string Current { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Error { get; set; }

    public static ActionState Unreadable(string error, string target = "") => new() { Readable = false, Error = error, Target = target, Current = "?" };
    public static ActionState Absent(string reason, string target = "") => new() { Readable = false, NotApplicable = true, Error = reason, Target = target, Current = reason };
}

public sealed class ChangeEntry
{
    public int Index { get; set; }
    public ActionDefinition Action { get; set; } = new();
    public StateSnapshot? Before { get; set; }
    public bool Applied { get; set; }
    public bool Restored { get; set; }
    /// <summary>Restore/rollback verification: the captured state was read back after restoring.</summary>
    public bool Verified { get; set; }
    /// <summary>Post-apply verification: true = target value read back, false = not effective, null = not verifiable.</summary>
    public bool? ApplyVerified { get; set; }
    /// <summary>Value read back right after applying (shown in the verification report).</summary>
    public string? ObservedAfterApply { get; set; }
    public string? Error { get; set; }
}

/// <summary>One applied optimization (or one dynamic change such as a startup entry) in the journal.</summary>
public sealed class ChangeSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? BatchId { get; set; }
    public string? TweakId { get; set; }
    /// <summary>catalog | startup | service | task | display | gpu | other</summary>
    public string Origin { get; set; } = "catalog";
    public string Category { get; set; } = "";
    public LocalizedText Title { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RestoredUtc { get; set; }
    public ChangeSetStatus Status { get; set; } = ChangeSetStatus.Pending;
    public RestartRequirement Restart { get; set; }
    public bool Reversible { get; set; } = true;
    public string? Error { get; set; }
    public List<ChangeEntry> Entries { get; set; } = new();
}

/// <summary>A profile application (or any group of changes applied together).</summary>
public sealed class ChangeBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public LocalizedText Title { get; set; } = new();
    public ProfileLevel? Profile { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool RestorePointCreated { get; set; }
    public string? RestorePointNote { get; set; }
    public List<string> ChangeSetIds { get; set; } = new();
}

public interface IChangeJournal
{
    IReadOnlyList<ChangeSet> ChangeSets { get; }
    IReadOnlyList<ChangeBatch> Batches { get; }
    void Save(ChangeSet changeSet);
    void Save(ChangeBatch batch);
    ChangeSet? Find(string id);
    event EventHandler? Changed;
}
