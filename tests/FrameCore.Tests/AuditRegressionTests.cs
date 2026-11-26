using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Engine;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;
using FrameCore.HardwareDetection;
using FrameCore.MemoryManager;
using Xunit;

namespace FrameCore.Tests;

/// <summary>Regression tests for defects found by the real-Windows CI run and the audit.</summary>
public class AuditRegressionTests
{
    /// <summary>Writes its value, then throws (like a WMI Put that half-succeeds).</summary>
    private sealed class HalfWritingExecutor : IActionExecutor
    {
        public string Value = "orig";
        public string Type => "half";
        public bool RequiresElevation(ActionDefinition a) => false;
        public ActionState Read(ActionDefinition a) => new() { Matches = Value == "new", Current = Value, Target = "new" };
        public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of(Value);
        public void Apply(ActionDefinition a) { Value = "new"; throw new IOException("Generic failure"); }
        public void Restore(ActionDefinition a, StateSnapshot s) => Value = s.Value!;
        public bool Verify(ActionDefinition a, StateSnapshot s) => Value == s.Value;
    }

    /// <summary>Accepts the write but the value never changes (overridden by a policy).</summary>
    private sealed class IgnoredExecutor : IActionExecutor
    {
        public string Type => "ignored";
        public bool RequiresElevation(ActionDefinition a) => false;
        public ActionState Read(ActionDefinition a) => new() { Matches = false, Current = "0", Target = "1" };
        public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of("0");
        public void Apply(ActionDefinition a) { }
        public void Restore(ActionDefinition a, StateSnapshot s) { }
        public bool Verify(ActionDefinition a, StateSnapshot s) => true;
    }

    private sealed class MissingExecutor : IActionExecutor
    {
        public string Type => "missing";
        public bool RequiresElevation(ActionDefinition a) => false;
        public ActionState Read(ActionDefinition a) => new() { Matches = false, Current = "1", Target = "2" };
        public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of("1");
        public void Apply(ActionDefinition a) => throw new NotApplicableException("absent here");
        public void Restore(ActionDefinition a, StateSnapshot s) { }
        public bool Verify(ActionDefinition a, StateSnapshot s) => true;
    }

    private static (TweakEngine engine, HalfWritingExecutor half) Engine()
    {
        var half = new HalfWritingExecutor();
        var reg = new ExecutorRegistry(new IActionExecutor[] { half, new IgnoredExecutor(), new MissingExecutor() });
        return (new TweakEngine(new LocalDispatcher(reg), new InMemoryJournal()), half);
    }

    private static TweakDefinition Tweak(string type) => new()
    {
        Id = "t-" + type, Category = "Windows", Name = new("t", "t"), Description = new("d", "d"), Confidence = Confidence.Confirmed,
        Actions = { new ActionDefinition { Type = type } },
    };

    [Fact]
    public void A_failing_action_that_wrote_partially_is_rolled_back_too()
    {
        var (engine, half) = Engine();
        var r = engine.Apply(Tweak("half"), Sim.Facts(Sim.DesktopProfile()));
        Assert.False(r.Success);
        Assert.True(r.RolledBack);
        Assert.Equal("orig", half.Value);
    }

    [Fact]
    public void Value_not_read_back_after_apply_is_reported_as_not_effective()
    {
        var (engine, _) = Engine();
        var r = engine.Apply(Tweak("ignored"), Sim.Facts(Sim.DesktopProfile()));
        Assert.True(r.Success);
        Assert.False(r.Verified);
        Assert.False(r.ChangeSet!.Entries[0].ApplyVerified);
    }

    [Fact]
    public void Missing_target_is_reported_as_not_applicable_not_as_an_error()
    {
        var (engine, _) = Engine();
        var r = engine.Apply(Tweak("missing"), Sim.Facts(Sim.DesktopProfile()));
        Assert.False(r.Success);
        Assert.True(r.NotApplicable);
    }

    [Fact]
    public void Verified_change_reads_back_its_target()
    {
        var sim = new Sim();
        var tweak = new TweakDefinition
        {
            Id = "reg", Category = "Windows", Name = new("r", "r"), Description = new("d", "d"), Confidence = Confidence.Confirmed,
            Actions = { new ActionDefinition { Type = "registry", Path = @"HKCU\Software\Test", Name = "V", ValueKind = "DWord", Value = Sim.J(1) } },
        };
        var r = sim.Engine.Apply(tweak, Sim.Facts(Sim.DesktopProfile()));
        Assert.True(r.Success);
        Assert.True(r.Verified);
    }

    [Fact]
    public void Windows_server_is_not_judged_with_the_client_lifecycle()
    {
        Repo.LoadLoc();
        var (version, ended, _) = WindowsVersions.Evaluate(26100, "ServerDatacenter", new DateTime(2026, 11, 26));
        Assert.Null(version);
        Assert.Null(ended);
    }

    [Fact]
    public void Virtual_machines_get_no_channel_or_xmp_guess()
    {
        Repo.LoadLoc();
        var mem = new MemoryInfo { Modules = { new MemoryModule { CapacityBytes = 8L << 30 }, new MemoryModule { CapacityBytes = 8L << 30 } } };
        MemoryConfigAnalyzer.Analyze(mem, laptop: false, virtualMachine: true);
        Assert.Equal(ChannelMode.Unknown, mem.Channels);
        Assert.Equal(DataConfidence.Unavailable, mem.ChannelsConfidence);
        Assert.True(BoardInfo.LooksVirtual("Microsoft Corporation", "Virtual Machine"));
        Assert.False(BoardInfo.LooksVirtual("ASUSTeK COMPUTER INC.", "ROG STRIX B650E-F GAMING WIFI"));
    }

    [Fact]
    public void Two_modules_without_slot_names_are_only_probably_dual_channel()
    {
        Repo.LoadLoc();
        var mem = new MemoryInfo { Modules = { new MemoryModule { CapacityBytes = 8L << 30 }, new MemoryModule { CapacityBytes = 8L << 30 } } };
        MemoryConfigAnalyzer.Analyze(mem, laptop: false);
        Assert.Equal(ChannelMode.Dual, mem.Channels);
        Assert.Equal(DataConfidence.Inferred, mem.ChannelsConfidence);
    }

    [Fact]
    public void Data_quality_table_flags_unavailable_values()
    {
        Repo.LoadLoc();
        var p = Sim.DesktopProfile();
        p.Board.SystemManufacturer = "Microsoft Corporation"; p.Board.SystemModel = "Virtual Machine";
        var dq = DataQualityAssessor.Assess(p);
        Assert.Contains(dq, d => d.Area == "ram" && d.Confidence == DataConfidence.Unavailable);
        Assert.Contains(dq, d => d.Area == "cpu" && d.Confidence == DataConfidence.Measured);
    }
}
