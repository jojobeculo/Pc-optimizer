using System.Text.Json;
using FrameCore.Core.Models;

namespace FrameCore.Core.Catalog;

/// <summary>Loads and validates the declarative optimization catalog.</summary>
public sealed class TweakCatalog
{
    public static readonly string[] KnownActionTypes =
    {
        "registry", "service", "task", "powerSetting", "powerScheme", "powerOverlay", "bcdRemove",
        "spi", "dxSetting", "optionalFeature", "netAdapterProperty", "handler",
    };

    public static readonly string[] KnownCategories =
    {
        "Windows", "Services", "Startup", "Cpu", "Gpu", "Ram", "Bios", "Drivers", "Storage", "Network", "Display", "Input", "Power", "Privacy", "Security",
    };

    private readonly Dictionary<string, TweakDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<TweakDefinition> Tweaks { get; }
    public IReadOnlyList<string> LoadErrors { get; }

    private TweakCatalog(List<TweakDefinition> tweaks, List<string> errors)
    {
        Tweaks = tweaks;
        LoadErrors = errors;
        foreach (var t in tweaks) _byId[t.Id] = t;
    }

    public TweakDefinition? Find(string id) => _byId.TryGetValue(id, out var t) ? t : null;

    public static TweakCatalog LoadFromDirectory(string directory)
    {
        var tweaks = new List<TweakDefinition>();
        var errors = new List<string>();
        var tweakDir = Path.Combine(directory, "tweaks");
        if (!Directory.Exists(tweakDir))
        {
            errors.Add($"Catalog directory not found: {tweakDir}");
            return new TweakCatalog(tweaks, errors);
        }
        foreach (var file in Directory.GetFiles(tweakDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<TweakCatalogFile>(File.ReadAllText(file), Json.Options);
                if (parsed?.Tweaks != null) tweaks.AddRange(parsed.Tweaks);
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        errors.AddRange(Validate(tweaks));
        // Invalid entries are dropped rather than half-applied.
        var invalidIds = new HashSet<string>(errors.Select(e => e.Split(':')[0]), StringComparer.OrdinalIgnoreCase);
        return new TweakCatalog(tweaks.Where(t => !invalidIds.Contains(t.Id)).ToList(), errors);
    }

    public static TweakCatalog FromTweaks(IEnumerable<TweakDefinition> tweaks)
    {
        var list = tweaks.ToList();
        return new TweakCatalog(list, Validate(list).ToList());
    }

    /// <summary>Structural validation; also enforces the safety policy (see <see cref="SafetyPolicy"/>).</summary>
    public static IEnumerable<string> Validate(IReadOnlyList<TweakDefinition> tweaks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = tweaks.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tweaks)
        {
            foreach (var other in t.Conflicts.Concat(t.Requires))
                if (!ids.Contains(other)) yield return $"{t.Id}: unknown related id {other}";
            foreach (var other in t.Conflicts)
                if (tweaks.FirstOrDefault(x => x.Id == other) is { } o && !o.Conflicts.Contains(t.Id)) yield return $"{t.Id}: conflict with {other} must be declared on both sides";
            if (t.ImpactAreas.Count == 0) yield return $"{t.Id}: impactAreas missing";
            if (!t.Reversible && t.Persistence != Persistence.OneShot) yield return $"{t.Id}: non-reversible operations must be one-shot";
            if (string.IsNullOrWhiteSpace(t.Id)) { yield return "(no id): missing id"; continue; }
            if (!seen.Add(t.Id)) yield return $"{t.Id}: duplicate id";
            if (t.Name.IsEmpty) yield return $"{t.Id}: missing name";
            if (t.Description.IsEmpty) yield return $"{t.Id}: missing description";
            if (!KnownCategories.Contains(t.Category)) yield return $"{t.Id}: unknown category {t.Category}";
            if (t.Actions.Count == 0) yield return $"{t.Id}: no action";
            if (t.Confidence == Confidence.NotRecommended && t.Profiles.Count > 0)
                yield return $"{t.Id}: a not-recommended tweak cannot belong to a profile";
            if (t.Confidence == Confidence.Experimental && t.Profiles.Any(p => p != ProfileLevel.Maximum))
                yield return $"{t.Id}: experimental tweaks may only appear (unselected) in the Maximum profile";
            foreach (var c in t.Conditions)
            {
                if (c.Any == null && !ConditionEvaluator.Operators.Contains(c.Op)) yield return $"{t.Id}: unknown operator {c.Op}";
                if (c.Any == null && string.IsNullOrWhiteSpace(c.Fact)) yield return $"{t.Id}: condition without fact";
            }
            foreach (var a in t.Actions)
            {
                if (!KnownActionTypes.Contains(a.Type)) { yield return $"{t.Id}: unknown action type {a.Type}"; continue; }
                foreach (var err in SafetyPolicy.Check(a)) yield return $"{t.Id}: {err}";
                switch (a.Type)
                {
                    case "registry":
                        if (string.IsNullOrWhiteSpace(a.Path) || string.IsNullOrWhiteSpace(a.Name)) yield return $"{t.Id}: registry action needs path and name";
                        else if (!a.Path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) && !a.Path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
                            yield return $"{t.Id}: registry path must start with HKCU\\ or HKLM\\";
                        if (!a.Delete && (a.ValueKind == null || a.Value == null)) yield return $"{t.Id}: registry action needs valueKind and value";
                        break;
                    case "service":
                        if (string.IsNullOrWhiteSpace(a.Name) || a.StartType is not ("Automatic" or "AutomaticDelayed" or "Manual" or "Disabled"))
                            yield return $"{t.Id}: service action needs name and a valid startType";
                        break;
                    case "task":
                        if (string.IsNullOrWhiteSpace(a.Path) || a.Enabled == null) yield return $"{t.Id}: task action needs path and enabled";
                        break;
                    case "powerSetting":
                        if (!Guid.TryParse(a.Subgroup, out _) || !Guid.TryParse(a.Setting, out _) || (a.Ac == null && a.Dc == null))
                            yield return $"{t.Id}: powerSetting needs subgroup/setting GUIDs and ac or dc";
                        break;
                    case "powerScheme":
                    case "powerOverlay":
                        if (!Guid.TryParse(a.Scheme, out _)) yield return $"{t.Id}: {a.Type} needs a scheme GUID";
                        break;
                    case "handler":
                    case "spi":
                    case "dxSetting":
                    case "optionalFeature":
                    case "bcdRemove":
                    case "netAdapterProperty":
                        if (string.IsNullOrWhiteSpace(a.Name)) yield return $"{t.Id}: {a.Type} needs a name";
                        break;
                }
            }
        }
    }
}
