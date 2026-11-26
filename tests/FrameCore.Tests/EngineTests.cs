using FrameCore.BackupManager;
using FrameCore.Core.Catalog;
using FrameCore.Core.Elevation;
using FrameCore.Core.Engine;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.WindowsOptimization.Executors;
using FrameCore.WindowsOptimization.Platform;
using Xunit;

namespace FrameCore.Tests;

public class EngineTests
{
    private static TweakDefinition Tweak(string id, params ActionDefinition[] actions) => new()
    {
        Id = id, Category = "Windows", Name = new(id, id), Description = new("d", "d"), Confidence = Confidence.Confirmed, Actions = actions.ToList(),
    };

    private static ActionDefinition Reg(string path, string name, object value, string kind = "DWord") =>
        new() { Type = "registry", Path = path, Name = name, ValueKind = kind, Value = Sim.J(value) };

    [Fact]
    public void Apply_then_restore_returns_the_exact_previous_values_including_absent_ones()
    {
        var sim = new Sim();
        sim.Registry.SetValue(@"HKCU\Test", "Existing", "DWord", 7);
        var t = Tweak("t1", Reg(@"HKCU\Test", "Existing", 1), Reg(@"HKCU\Test", "New", 2));

        var r = sim.Engine.Apply(t, new MachineFacts());
        Assert.True(r.Success);
        Assert.Equal(1, sim.Registry.GetValue(@"HKCU\Test", "Existing")!.Data);
        Assert.Equal(2, sim.Registry.GetValue(@"HKCU\Test", "New")!.Data);
        Assert.Equal(ChangeSetStatus.Applied, r.ChangeSet!.Status);
        Assert.Equal(TweakStatus.Applied, sim.Engine.Evaluate(t, new MachineFacts()).Status);

        var restore = sim.Engine.Restore(r.ChangeSet);
        Assert.True(restore.Success);
        Assert.Equal(7, sim.Registry.GetValue(@"HKCU\Test", "Existing")!.Data); // real previous value, not a default
        Assert.Null(sim.Registry.GetValue(@"HKCU\Test", "New"));                 // did not exist → deleted
        Assert.All(r.ChangeSet.Entries, e => Assert.True(e.Verified));
        Assert.Equal(ChangeSetStatus.Restored, r.ChangeSet.Status);
    }

    [Fact]
    public void Failure_in_the_middle_rolls_back_already_applied_actions()
    {
        var sim = new Sim();
        sim.Registry.SetValue(@"HKCU\Test", "A", "DWord", 5);
        var t = Tweak("t2", Reg(@"HKCU\Test", "A", 9), new ActionDefinition { Type = "fail", Name = "x" }, Reg(@"HKCU\Test", "C", 1));

        var r = sim.Engine.Apply(t, new MachineFacts());
        Assert.False(r.Success);
        Assert.True(r.RolledBack);
        Assert.Equal(5, sim.Registry.GetValue(@"HKCU\Test", "A")!.Data);
        Assert.Null(sim.Registry.GetValue(@"HKCU\Test", "C"));
        Assert.Equal(ChangeSetStatus.RolledBack, r.ChangeSet!.Status);
    }

    [Fact]
    public void Journal_is_persisted_before_any_modification()
    {
        var sim = new Sim();
        sim.Registry.DenyWrites.Add(@"HKCU\Test\A");
        var t = Tweak("t3", Reg(@"HKCU\Test", "A", 1));
        var r = sim.Engine.Apply(t, new MachineFacts());
        Assert.False(r.Success);
        Assert.True(sim.Journal.SaveCount >= 2); // pending save happened before the failing write
        Assert.NotNull(sim.Journal.Find(r.ChangeSet!.Id));
    }

    [Fact]
    public void Incompatible_tweak_is_refused_without_touching_anything()
    {
        var sim = new Sim();
        var t = Tweak("t4", Reg(@"HKCU\Test", "A", 1));
        t.Conditions.Add(new Condition { Fact = "system.isDesktop", Op = "isTrue" });
        var r = sim.Engine.Apply(t, new MachineFacts().Set("system.isDesktop", false));
        Assert.False(r.Success);
        Assert.Null(sim.Registry.GetValue(@"HKCU\Test", "A"));
        Assert.Empty(sim.Journal.ChangeSets);
    }

    [Fact]
    public void Service_task_and_power_changes_are_restored_to_captured_state()
    {
        var sim = new Sim();
        sim.Services.Services["DiagTrack"] = "AutomaticDelayed";
        sim.Tasks.Tasks[@"\Microsoft\Windows\Maps\MapsUpdateTask"] = true;
        var sub = "54533251-82be-4824-96c1-47eeb4c3c3a1"; var set = "bc5038f7-23e0-4960-96da-33abaf5935ec";
        sim.Power.WriteAc(PowerGuids.Balanced, Guid.Parse(sub), Guid.Parse(set), 80);
        var t = Tweak("t5",
            new ActionDefinition { Type = "service", Name = "DiagTrack", StartType = "Disabled" },
            new ActionDefinition { Type = "task", Path = @"\Microsoft\Windows\Maps\MapsUpdateTask", Enabled = false },
            new ActionDefinition { Type = "powerSetting", Subgroup = sub, Setting = set, Ac = 100 });
        var r = sim.Engine.Apply(t, new MachineFacts());
        Assert.True(r.Success);
        Assert.Equal("Disabled", sim.Services.Services["DiagTrack"]);
        Assert.False(sim.Tasks.Tasks[@"\Microsoft\Windows\Maps\MapsUpdateTask"]);
        Assert.Equal(100u, sim.Power.ReadAc(PowerGuids.Balanced, Guid.Parse(sub), Guid.Parse(set)));

        Assert.True(sim.Engine.Restore(r.ChangeSet!).Success);
        Assert.Equal("AutomaticDelayed", sim.Services.Services["DiagTrack"]);
        Assert.True(sim.Tasks.Tasks[@"\Microsoft\Windows\Maps\MapsUpdateTask"]);
        Assert.Equal(80u, sim.Power.ReadAc(PowerGuids.Balanced, Guid.Parse(sub), Guid.Parse(set)));
    }

    [Fact]
    public void Ultimate_plan_is_duplicated_then_removed_on_restore()
    {
        var sim = new Sim();
        var ultimate = PowerGuids.UltimatePerformance;
        var t = Tweak("t6", new ActionDefinition { Type = "powerScheme", Scheme = ultimate.ToString(), DuplicateFrom = ultimate.ToString() });
        var r = sim.Engine.Apply(t, new MachineFacts());
        Assert.True(r.Success);
        Assert.Equal(ultimate, sim.Power.Active);
        sim.Engine.Restore(r.ChangeSet!);
        Assert.Equal(PowerGuids.Balanced, sim.Power.Active);
        Assert.DoesNotContain(ultimate, sim.Power.Schemes);
    }

    [Fact]
    public void DirectX_global_settings_are_merged_and_restored()
    {
        var sim = new Sim();
        sim.Registry.SetValue(DxSettingExecutor.KeyPath, DxSettingExecutor.ValueName, "String", "VRROptimizeEnable=0;");
        var t = Tweak("t7", new ActionDefinition { Type = "dxSetting", Name = "SwapEffectUpgradeEnable", Value = Sim.J("1") });
        var r = sim.Engine.Apply(t, new MachineFacts());
        Assert.True(r.Success);
        var s = (string)sim.Registry.GetValue(DxSettingExecutor.KeyPath, DxSettingExecutor.ValueName)!.Data;
        Assert.Contains("VRROptimizeEnable=0;", s);
        Assert.Contains("SwapEffectUpgradeEnable=1;", s);
        sim.Engine.Restore(r.ChangeSet!);
        Assert.Equal("VRROptimizeEnable=0;", sim.Registry.GetValue(DxSettingExecutor.KeyPath, DxSettingExecutor.ValueName)!.Data);
    }

    [Fact]
    public void Real_catalog_entries_apply_and_restore_on_a_simulated_machine()
    {
        var sim = new Sim();
        var catalog = TweakCatalog.LoadFromDirectory(Repo.Catalog);
        var facts = Sim.Facts(Sim.DesktopProfile());
        foreach (var id in new[] { "gaming.gamemode", "visual.transparency", "content.suggestions", "privacy.adid", "graphics.hags" })
        {
            var t = catalog.Find(id)!;
            var r = sim.Engine.Apply(t, facts);
            Assert.True(r.Success, id + ": " + r.Error);
            Assert.Equal(TweakStatus.Applied, sim.Engine.Evaluate(t, facts).Status);
            Assert.True(sim.Engine.Restore(r.ChangeSet!).Success, id);
            Assert.All(t.Actions.Where(a => a.Type == "registry"), a => Assert.Null(sim.Registry.GetValue(a.Path!, a.Name!)));
        }
    }

    [Fact]
    public void Restore_many_processes_most_recent_first()
    {
        var sim = new Sim();
        sim.Registry.SetValue(@"HKCU\T", "V", "DWord", 1);
        var a = sim.Engine.Apply(Tweak("a", Reg(@"HKCU\T", "V", 2)), new MachineFacts());
        var b = sim.Engine.Apply(Tweak("b", Reg(@"HKCU\T", "V", 3)), new MachineFacts());
        b.ChangeSet!.CreatedUtc = a.ChangeSet!.CreatedUtc.AddSeconds(1);
        var r = sim.Engine.RestoreMany(new[] { a.ChangeSet, b.ChangeSet });
        Assert.True(r.Success);
        Assert.Equal(1, sim.Registry.GetValue(@"HKCU\T", "V")!.Data);
    }

    [Fact]
    public void Plan_builder_separates_experimental_and_consent_items()
    {
        var catalog = TweakCatalog.LoadFromDirectory(Repo.Catalog);
        var sim = new Sim();
        var facts = Sim.Facts(Sim.DesktopProfile());
        var evals = catalog.Tweaks.Select(t => new TweakEvaluation
        {
            Tweak = t, Compatible = ConditionEvaluator.Evaluate(t.Conditions, facts).Passed, Status = TweakStatus.NotApplied,
        }).ToList();
        var max = PlanBuilder.Build(ProfileLevel.Maximum, evals);
        Assert.Contains(max.Items, i => i.Section == PlanSection.Experimental && !i.SelectedByDefault);
        Assert.Contains(max.Items, i => i.Tweak.Id == "security.vmp" && i.Section == PlanSection.Consent && !i.SelectedByDefault);
        Assert.DoesNotContain(max.Items, i => i.Tweak.Confidence == Confidence.NotRecommended);
        var light = PlanBuilder.Build(ProfileLevel.Light, evals);
        Assert.All(light.Items, i => Assert.Equal(PlanSection.Standard, i.Section));
        Assert.True(light.Items.Count < max.Items.Count);
    }
}

public class SecurityTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadFromDirectory(Repo.Catalog);

    [Fact]
    public void Worker_accepts_exact_catalog_actions_only()
    {
        var v = new PrivilegedRequestValidator(Catalog);
        var t = Catalog.Find("svc.diagtrack")!;
        v.Validate(new PrivilegedActionRequest("apply", t.Actions[0], null, new ActionContext(t.Id, "catalog")));
        var tampered = ActionDefinition.Clone(t.Actions[0]);
        tampered.Name = "WSearch";
        Assert.Throws<UnauthorizedAccessException>(() => v.Validate(new PrivilegedActionRequest("apply", tampered, null, new ActionContext(t.Id, "catalog"))));
        Assert.Throws<UnauthorizedAccessException>(() => v.Validate(new PrivilegedActionRequest("apply", t.Actions[0], null, new ActionContext("does.not.exist", "catalog"))));
    }

    [Fact]
    public void Worker_restricts_dynamic_changes()
    {
        var v = new PrivilegedRequestValidator(Catalog);
        var approval = new ActionDefinition { Type = "registry", Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", Name = "App", ValueKind = "Binary", Value = Sim.J("030000000000000000000000") };
        v.Validate(new PrivilegedActionRequest("apply", approval, null, new ActionContext(null, "startup")));
        var arbitrary = new ActionDefinition { Type = "registry", Path = @"HKLM\SYSTEM\CurrentControlSet\Services\Foo", Name = "Start", ValueKind = "DWord", Value = Sim.J(4) };
        Assert.Throws<UnauthorizedAccessException>(() => v.Validate(new PrivilegedActionRequest("apply", arbitrary, null, new ActionContext(null, "startup"))));
        var defender = new ActionDefinition { Type = "service", Name = "WinDefend", StartType = "Disabled" };
        Assert.Throws<UnauthorizedAccessException>(() => v.Validate(new PrivilegedActionRequest("apply", defender, null, new ActionContext(null, "service"))));
        var vgc = new ActionDefinition { Type = "service", Name = "vgc", StartType = "Disabled" };
        Assert.Throws<UnauthorizedAccessException>(() => v.Validate(new PrivilegedActionRequest("apply", vgc, null, new ActionContext(null, "service"))));
        var unknownOrigin = new ActionDefinition { Type = "service", Name = "SomeVendorSvc", StartType = "Manual" };
        Assert.Throws<UnauthorizedAccessException>(() => v.Validate(new PrivilegedActionRequest("apply", unknownOrigin, null, new ActionContext(null, "script"))));
        v.Validate(new PrivilegedActionRequest("apply", unknownOrigin, null, new ActionContext(null, "service")));
    }

    [Fact]
    public void Safety_policy_blocks_security_targets()
    {
        Assert.True(SafetyPolicy.IsProtectedRegistry(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring"));
        Assert.True(SafetyPolicy.IsProtectedRegistry(@"HKLM\SYSTEM\ControlSet001\Control\DeviceGuard", "Enabled"));
        Assert.True(SafetyPolicy.IsProtectedRegistry(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride"));
        Assert.False(SafetyPolicy.IsProtectedRegistry(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled"));
        Assert.True(SafetyPolicy.IsProtectedTask(@"\Microsoft\Windows\WindowsUpdate\Scheduled Start"));
        Assert.NotEmpty(SafetyPolicy.Check(new ActionDefinition { Type = "bcdRemove", Name = "testsigning" }));
        Assert.Empty(SafetyPolicy.Check(new ActionDefinition { Type = "bcdRemove", Name = "useplatformclock" }));
        Assert.NotEmpty(SafetyPolicy.Check(new ActionDefinition { Type = "task", Path = @"\Microsoft\Windows\Defrag\ScheduledDefrag", Enabled = false }));
        Assert.NotEmpty(SafetyPolicy.Check(new ActionDefinition { Type = "optionalFeature", Name = "Windows-Defender-Default-Definitions" }));
    }

    [Fact]
    public async Task Pipe_protocol_round_trips_typed_payloads()
    {
        using var ms = new MemoryStream();
        var req = new PipeRequest { Id = 3, Op = "action.apply", Payload = PipeProtocol.ToElement(new PrivilegedActionRequest("apply", new ActionDefinition { Type = "service", Name = "X", StartType = "Manual" }, null, new ActionContext("t", "catalog"))) };
        await PipeProtocol.WriteAsync(ms, req);
        ms.Position = 0;
        var back = await PipeProtocol.ReadAsync<PipeRequest>(ms);
        Assert.Equal("action.apply", back!.Op);
        var payload = PipeProtocol.FromElement<PrivilegedActionRequest>(back.Payload)!;
        Assert.Equal("Manual", payload.Action.StartType);
        Assert.Equal("t", payload.Context.TweakId);
    }
}

public class JournalTests
{
    [Fact]
    public void Journal_survives_restart_and_marks_interrupted_sets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var j = new JsonChangeJournal(dir);
            var cs = new ChangeSet { TweakId = "x", Title = new("t", "t"), Status = ChangeSetStatus.Pending };
            cs.Entries.Add(new ChangeEntry { Action = new ActionDefinition { Type = "registry", Path = @"HKCU\A", Name = "B" }, Before = StateSnapshot.Of("1", "DWord"), Applied = true });
            j.Save(cs);
            j.Save(new ChangeBatch { Title = new("b", "b"), ChangeSetIds = { cs.Id } });

            var reloaded = new JsonChangeJournal(dir);
            var r = reloaded.Find(cs.Id)!;
            Assert.Equal(ChangeSetStatus.Failed, r.Status); // was pending with an applied entry → interrupted
            Assert.Equal("1", r.Entries[0].Before!.Value);
            Assert.Single(reloaded.Batches);

            var zip = Path.Combine(dir, "..", Path.GetFileName(dir) + ".zip");
            reloaded.Export(zip);
            var other = new JsonChangeJournal(dir + "-2");
            Assert.Equal(1, other.Import(zip));
            Assert.NotNull(other.Find(cs.Id));
            File.Delete(zip);
        }
        finally
        {
            try { Directory.Delete(dir, true); Directory.Delete(dir + "-2", true); } catch { }
        }
    }
}
