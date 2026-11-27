using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameCore.Core.Models;

/// <summary>One optimization of the declarative catalog (catalog/tweaks/*.json).</summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    /// <summary>Pro-mode category: Windows, Services, Startup, Cpu, Gpu, Ram, Bios, Drivers, Storage, Network, Display.</summary>
    public string Category { get; set; } = "Windows";
    public LocalizedText Group { get; set; } = new();
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public LocalizedText Impact { get; set; } = new();
    public LocalizedText Regressions { get; set; } = new();
    public LocalizedText? Notes { get; set; }
    /// <summary>What the change is meant to achieve, in one sentence.</summary>
    public LocalizedText? Objective { get; set; }
    public Persistence Persistence { get; set; } = Persistence.Persistent;
    /// <summary>Mutually exclusive optimisations (e.g. two power plans). A plan never contains both.</summary>
    public List<string> Conflicts { get; set; } = new();
    /// <summary>Optimisations that must be active for this one to make sense.</summary>
    public List<string> Requires { get; set; } = new();
    /// <summary>Areas this change can influence (FPS, frame times, input latency, network latency, privacy…).</summary>
    public List<ImpactArea> ImpactAreas { get; set; } = new();
    public Confidence Confidence { get; set; }
    public RiskLevel Risk { get; set; }
    public TweakKind Kind { get; set; }
    public RestartRequirement Restart { get; set; }
    /// <summary>Simple-mode profiles that include this optimization. Empty = Pro mode only.</summary>
    public List<ProfileLevel> Profiles { get; set; } = new();
    /// <summary>Requires an explicit, specific confirmation even inside a profile.</summary>
    public bool RequiresConsent { get; set; }
    /// <summary>False for stateless maintenance operations (cleanup, ReTrim).</summary>
    public bool Reversible { get; set; } = true;
    /// <summary>All conditions must pass for the optimization to be compatible.</summary>
    public List<Condition> Conditions { get; set; } = new();
    public List<string> Sources { get; set; } = new();
    public List<ActionDefinition> Actions { get; set; } = new();
    /// <summary>
    /// Actions of earlier versions of this optimisation. Never applied: they are kept so that a change made by an older
    /// FrameCore version can still be restored (read, restored, verified) by the elevated worker, however long ago.
    /// </summary>
    public List<ActionDefinition> RetiredActions { get; set; } = new();

    [JsonIgnore] public bool IsExperimental => Confidence == Confidence.Experimental;
    [JsonIgnore] public bool IsProOnly => Profiles.Count == 0;
    /// <summary>True when the change has a technical reason to influence frame rate or frame pacing.</summary>
    [JsonIgnore] public bool CanAffectFrameRate => ImpactAreas.Contains(ImpactArea.Fps) || ImpactAreas.Contains(ImpactArea.FrameTimes);
}

/// <summary>
/// A closed set of action types (whitelist). There is intentionally no "run script" type.
/// Fields are interpreted per <see cref="Type"/>; executors validate what they need.
/// </summary>
public sealed class ActionDefinition
{
    /// <summary>registry | service | task | powerSetting | powerScheme | powerOverlay | bcdRemove | spi | dxSetting | optionalFeature | netAdapterProperty | handler</summary>
    public string Type { get; set; } = "";
    public string? Path { get; set; }
    public string? Name { get; set; }
    /// <summary>Registry value kind: DWord, QWord, String, ExpandString, MultiString, Binary.</summary>
    public string? ValueKind { get; set; }
    public JsonElement? Value { get; set; }
    /// <summary>Registry: target is "value absent".</summary>
    public bool Delete { get; set; }
    public string? StartType { get; set; }
    public bool? Enabled { get; set; }
    public string? Subgroup { get; set; }
    public string? Setting { get; set; }
    public int? Ac { get; set; }
    public int? Dc { get; set; }
    public string? Scheme { get; set; }
    public string? DuplicateFrom { get; set; }
    public string? Filter { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }

    public string? ValueAsString()
    {
        if (Value is not { } v) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Array => string.Join("\n", v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText(),
        };
    }

    public static ActionDefinition Clone(ActionDefinition a) =>
        JsonSerializer.Deserialize<ActionDefinition>(JsonSerializer.Serialize(a, Json.Options), Json.Options)!;

    /// <summary>Stable canonical form, used by the elevated worker to compare a request with the catalog.</summary>
    public string Canonical() => JsonSerializer.Serialize(this, Json.Canonical);

    public override string ToString() => Type switch
    {
        "registry" => $"{Path}\\{Name}{(Delete ? " (delete)" : $" = {ValueAsString()}")}",
        "service" => $"service {Name} → {StartType}",
        "task" => $"task {Path} → {(Enabled == true ? "enabled" : "disabled")}",
        "powerSetting" => $"power {Subgroup}/{Setting} AC={Ac} DC={Dc}",
        "powerScheme" => $"power scheme {Scheme}",
        "handler" => $"handler {Name}",
        _ => $"{Type} {Name}",
    };
}

public sealed class Condition
{
    /// <summary>Fact key, e.g. "os.build", "cpu.isDualCcdX3D".</summary>
    public string Fact { get; set; } = "";
    /// <summary>==, !=, &gt;=, &lt;=, &gt;, &lt;, in, notIn, contains, exists, isTrue, isFalse</summary>
    public string Op { get; set; } = "isTrue";
    public JsonElement? Value { get; set; }
    /// <summary>Explanation shown when the condition is not met.</summary>
    public LocalizedText? Reason { get; set; }
    /// <summary>Alternative: at least one of these must pass.</summary>
    public List<Condition>? Any { get; set; }
}

public sealed class TweakCatalogFile
{
    public int SchemaVersion { get; set; } = 1;
    public List<TweakDefinition> Tweaks { get; set; } = new();
}
