using System.Text.Json;
using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Elevation;
using FrameCore.Core.Engine;
using FrameCore.Core.Models;
using Xunit;

namespace FrameCore.Tests;

/// <summary>
/// A change applied months ago, possibly by an older FrameCore version, must stay restorable as long as FrameCore is
/// installed. The elevated worker only accepts catalog actions, so every action a released version could write is frozen
/// here: a future version that changes or removes one must keep it in "retiredActions" (or catalog/retired.json).
/// </summary>
public class LongTermRestoreTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadFromDirectory(Repo.Catalog);
    private static string FixtureDir => Path.Combine(Repo.Root, "tests", "FrameCore.Tests", "fixtures");
    private sealed record Frozen(string TweakId, string Action);

    private static List<Frozen> Current() =>
        Catalog.Tweaks.SelectMany(t => t.Actions.Select(a => new Frozen(t.Id, a.Canonical()))).OrderBy(f => f.TweakId).ThenBy(f => f.Action).ToList();

    [Fact]
    public void Every_action_of_every_released_version_stays_restorable_by_the_elevated_worker()
    {
        var current = Path.Combine(FixtureDir, "restorable-actions-2.1.0.json");
        if (Environment.GetEnvironmentVariable("FRAMECORE_WRITE_FIXTURE") == "1")
            File.WriteAllText(current, JsonSerializer.Serialize(Current(), new JsonSerializerOptions { WriteIndented = true }));
        var validator = new PrivilegedRequestValidator(Catalog);
        var refused = new List<string>();
        foreach (var file in Directory.GetFiles(FixtureDir, "restorable-actions-*.json"))
            foreach (var f in JsonSerializer.Deserialize<List<Frozen>>(File.ReadAllText(file))!)
            {
                var action = JsonSerializer.Deserialize<ActionDefinition>(f.Action, Json.Canonical)!;
                foreach (var op in new[] { "read", "restore", "verify" })
                {
                    try { validator.Validate(new PrivilegedActionRequest(op, action, StateSnapshot.Absent(), new ActionContext(f.TweakId, "catalog"))); }
                    catch (UnauthorizedAccessException ex) { refused.Add($"{Path.GetFileName(file)} {f.TweakId} {op}: {ex.Message}"); }
                }
            }
        Assert.True(refused.Count == 0, "No longer restorable:\n" + string.Join("\n", refused.Take(20)));
    }

    [Fact]
    public void A_retired_action_can_be_restored_but_never_applied()
    {
        var oldAction = new ActionDefinition { Type = "registry", Path = @"HKCU\Software\FrameCoreTest", Name = "Old", ValueKind = "DWord", Value = JsonDocument.Parse("1").RootElement.Clone() };
        var tweak = new TweakDefinition
        {
            Id = "test.changed", Name = new("t", "t"), Description = new("d", "d"), Impact = new("i", "i"), Regressions = new("r", "r"),
            Actions = { new ActionDefinition { Type = "registry", Path = @"HKCU\Software\FrameCoreTest", Name = "New", ValueKind = "DWord", Value = JsonDocument.Parse("1").RootElement.Clone() } },
            RetiredActions = { oldAction },
        };
        var validator = new PrivilegedRequestValidator(TweakCatalog.FromTweaks(new[] { tweak }));
        var ctx = new ActionContext("test.changed", "catalog");
        validator.Validate(new PrivilegedActionRequest("restore", oldAction, StateSnapshot.Absent(), ctx));
        validator.Validate(new PrivilegedActionRequest("verify", oldAction, StateSnapshot.Absent(), ctx));
        Assert.Throws<UnauthorizedAccessException>(() => validator.Validate(new PrivilegedActionRequest("apply", oldAction, null, ctx)));
        Assert.Throws<UnauthorizedAccessException>(() => validator.Validate(new PrivilegedActionRequest("restore", oldAction, StateSnapshot.Absent(), new ActionContext("unknown.id", "catalog"))));
    }

    [Fact]
    public void The_journal_keeps_old_changes_and_restores_them_from_the_saved_state_only()
    {
        // A change set written long ago is loaded and restored from its own saved "before" state: the catalog is not needed.
        Repo.LoadLoc();
        var dir = Path.Combine(Path.GetTempPath(), "fc-journal-" + Guid.NewGuid().ToString("N"));
        var reg = new FrameCore.Core.Platform.InMemoryRegistry();
        reg.SetValue(@"HKCU\Software\Game", "Mode", "DWord", 0);
        var executors = new ExecutorRegistry(new IActionExecutor[] { new FrameCore.WindowsOptimization.Executors.RegistryExecutor(reg) });
        var action = new ActionDefinition { Type = "registry", Path = @"HKCU\Software\Game", Name = "Mode", ValueKind = "DWord", Value = JsonDocument.Parse("1").RootElement.Clone() };
        var before = executors.Get("registry").Capture(action);
        executors.Get("registry").Apply(action);
        new FrameCore.BackupManager.JsonChangeJournal(dir).Save(new ChangeSet
        {
            TweakId = "removed.long.ago", Origin = "catalog", CreatedUtc = DateTime.UtcNow.AddMonths(-7), Status = ChangeSetStatus.Applied,
            Entries = { new ChangeEntry { Action = action, Before = before, Applied = true } },
        });

        var reloaded = new FrameCore.BackupManager.JsonChangeJournal(dir);
        var set = Assert.Single(reloaded.ChangeSets);
        var r = new TweakEngine(new LocalDispatcher(executors), reloaded).Restore(set);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal(0, Convert.ToInt32(reg.GetValue(@"HKCU\Software\Game", "Mode")!.Data));
        Assert.Equal(ChangeSetStatus.Restored, new FrameCore.BackupManager.JsonChangeJournal(dir).ChangeSets.Single().Status);
    }
}
