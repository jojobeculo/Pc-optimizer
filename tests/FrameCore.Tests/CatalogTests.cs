using System.Text.Json;
using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Models;
using Xunit;

namespace FrameCore.Tests;

public class CatalogTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadFromDirectory(Repo.Catalog);

    private static readonly HashSet<string> KnownHandlers = new()
    {
        "cleanup.temp.user", "cleanup.temp.system", "storage.retrim", "storage.pagefile.auto", "storage.hibernate.off", "display.maxRefresh", "net.autotuning.normal",
    };

    [Fact]
    public void Catalog_loads_without_errors()
    {
        Assert.Empty(Catalog.LoadErrors);
        Assert.True(Catalog.Tweaks.Count >= 50, $"only {Catalog.Tweaks.Count} tweaks");
    }

    [Fact]
    public void Every_tweak_is_documented_in_both_languages()
    {
        foreach (var t in Catalog.Tweaks)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name.Fr), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Name.En), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description.Fr), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description.En), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Impact.Fr), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Regressions.Fr), t.Id);
        }
    }

    [Fact]
    public void Profile_rules_are_respected()
    {
        foreach (var t in Catalog.Tweaks)
        {
            if (t.Confidence == Confidence.NotRecommended) Assert.Empty(t.Profiles);
            if (t.Confidence == Confidence.Experimental) Assert.All(t.Profiles, p => Assert.Equal(ProfileLevel.Maximum, p));
            // Light profile only contains low-risk, non-experimental changes.
            if (t.Profiles.Contains(ProfileLevel.Light))
            {
                Assert.True(t.Risk <= RiskLevel.Low, t.Id);
                Assert.True(t.Confidence is Confidence.Confirmed or Confidence.Probable, t.Id);
            }
        }
    }

    [Fact]
    public void No_catalog_action_targets_a_protected_component()
    {
        foreach (var a in Catalog.Tweaks.SelectMany(t => t.Actions))
            Assert.Empty(SafetyPolicy.Check(a));
    }

    [Fact]
    public void Forbidden_security_changes_are_absent()
    {
        var services = Catalog.Tweaks.SelectMany(t => t.Actions).Where(a => a.Type == "service").Select(a => a.Name!).ToList();
        foreach (var s in new[] { "WinDefend", "mpssvc", "wuauserv", "UsoSvc", "BFE", "SecurityHealthService", "BDESVC" })
            Assert.DoesNotContain(s, services, StringComparer.OrdinalIgnoreCase);
        var regs = Catalog.Tweaks.SelectMany(t => t.Actions).Where(a => a.Type == "registry").ToList();
        Assert.DoesNotContain(regs, a => a.Path!.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(regs, a => a.Name!.StartsWith("FeatureSettingsOverride", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(regs, a => a.Path!.Contains("DeviceGuard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Handlers_exist_and_maintenance_ops_are_flagged_non_reversible()
    {
        foreach (var t in Catalog.Tweaks)
            foreach (var a in t.Actions.Where(a => a.Type == "handler"))
            {
                Assert.Contains(a.Name!, KnownHandlers);
                if (a.Name!.StartsWith("cleanup") || a.Name == "storage.retrim") Assert.False(t.Reversible, t.Id);
            }
    }

    [Fact]
    public void Conditions_only_use_facts_produced_by_the_detection()
    {
        var facts = Sim.Facts(Sim.DesktopProfile());
        var known = facts.All.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<Condition> Flatten(Condition c) => c.Any == null ? new[] { c } : new[] { c }.Concat(c.Any.SelectMany(Flatten));
        foreach (var t in Catalog.Tweaks)
            foreach (var c in t.Conditions.SelectMany(Flatten).Where(c => c.Any == null))
                Assert.True(known.Contains(c.Fact), $"{t.Id}: unknown fact {c.Fact}");
    }

    [Fact]
    public void Dual_ccd_x3d_never_gets_power_plan_or_parking_changes()
    {
        var facts = Sim.Facts(Sim.DesktopProfile(dualX3D: true, vendor: Core.Hardware.CpuVendor.Amd));
        foreach (var id in new[] { "power.plan.high", "power.plan.ultimate", "power.coreparking", "gaming.dvr.capture" })
            Assert.False(ConditionEvaluator.Evaluate(Catalog.Find(id)!.Conditions, facts).Passed, id);
    }

    [Fact]
    public void Language_files_have_identical_keys()
    {
        var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "fr.json")))!;
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "en.json")))!;
        Assert.Empty(fr.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(fr.Keys));
        Assert.All(fr, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    [Fact]
    public void Format_placeholders_match_between_languages()
    {
        var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "fr.json")))!;
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "en.json")))!;
        static HashSet<string> Holes(string s) => System.Text.RegularExpressions.Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var (k, v) in fr) Assert.True(Holes(v).SetEquals(Holes(en[k])), k);
    }

    [Fact]
    public void Validation_rejects_dangerous_entries()
    {
        var bad = new TweakDefinition
        {
            Id = "bad", Category = "Windows", Name = new("x", "x"), Description = new("x", "x"), Confidence = Confidence.NotRecommended, Profiles = { ProfileLevel.Light },
            Actions = { new ActionDefinition { Type = "service", Name = "WinDefend", StartType = "Disabled" }, new ActionDefinition { Type = "bcdRemove", Name = "nx" } },
        };
        var errors = TweakCatalog.Validate(new[] { bad }).ToList();
        Assert.Contains(errors, e => e.Contains("protected"));
        Assert.Contains(errors, e => e.Contains("BCD element not allowed"));
        Assert.Contains(errors, e => e.Contains("not-recommended"));
    }
}
