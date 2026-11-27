using System.Text.Json;
using FrameCore.Core.Engine;
using FrameCore.Core.Models;
using FrameCore.WindowsOptimization.Executors;
using Xunit;

namespace FrameCore.Tests;

/// <summary>Extreme-level Defender settings: only scheduled-scan CPU and game-folder exclusions, restored exactly.</summary>
public class DefenderHandlerTests
{
    private sealed class FakeDefender : IDefenderPreferences
    {
        public bool Available { get; set; } = true;
        public bool Admin { get; set; } = true;
        public List<string> Exclusions { get; } = new();
        public int CpuLimit { get; set; } = 50;
        public IReadOnlyList<string>? ExclusionPaths() => Admin ? Exclusions.ToList() : null;
        public int? ScanCpuLimit() => Available ? CpuLimit : null;
        public void AddExclusionPaths(IReadOnlyList<string> paths) => Exclusions.AddRange(paths);
        public void RemoveExclusionPaths(IReadOnlyList<string> paths) => Exclusions.RemoveAll(e => paths.Contains(e, StringComparer.OrdinalIgnoreCase));
        public void SetScanCpuLimit(int percent) => CpuLimit = percent;
    }

    private static ActionDefinition Handler(string name, string? value = null) => new()
    {
        Type = "handler", Name = name,
        Value = value == null ? null : JsonDocument.Parse(value).RootElement.Clone(),
    };

    private static List<string> GameFolders(int n)
    {
        var root = Path.Combine(Path.GetTempPath(), "fc-games-" + Guid.NewGuid().ToString("N"));
        return Enumerable.Range(1, n).Select(i => Directory.CreateDirectory(Path.Combine(root, "lib" + i)).FullName).ToList();
    }

    [Fact]
    public void Scan_cpu_limit_is_applied_and_restored_to_the_previous_value()
    {
        var d = new FakeDefender { CpuLimit = 50 };
        var h = new DefenderScanCpuHandler(d);
        var a = Handler("defender.scanCpuLimit", "20");
        Assert.False(h.Read(a).Matches);
        var snap = h.Capture(a);
        h.Apply(a);
        Assert.Equal(20, d.CpuLimit);
        Assert.True(h.Read(a).Matches);
        h.Restore(a, snap);
        Assert.Equal(50, d.CpuLimit);
        Assert.True(h.Verify(a, snap));
    }

    [Fact]
    public void Zero_means_unlimited_and_is_not_already_in_place()
    {
        var d = new FakeDefender { CpuLimit = 0 };
        var h = new DefenderScanCpuHandler(d);
        var a = Handler("defender.scanCpuLimit", "20");
        Assert.False(h.Read(a).Matches);
        var snap = h.Capture(a);
        h.Apply(a);
        Assert.True(h.Read(a).Matches);
        h.Restore(a, snap);
        Assert.Equal(0, d.CpuLimit);
    }

    [Fact]
    public void Nothing_is_done_when_defender_is_not_the_active_antivirus()
    {
        var d = new FakeDefender { Available = false };
        var a = Handler("defender.scanCpuLimit", "20");
        Assert.Throws<NotApplicableException>(() => new DefenderScanCpuHandler(d).Apply(a));
        var g = new DefenderGameFolderExclusionsHandler(d, () => GameFolders(1));
        Assert.Throws<NotApplicableException>(() => g.Apply(Handler("defender.gameFolderExclusions")));
    }

    [Fact]
    public void Restoring_exclusions_removes_only_the_folders_frame_core_added()
    {
        var games = GameFolders(2);
        var d = new FakeDefender();
        d.Exclusions.Add(@"D:\MyOwnExclusion");
        d.Exclusions.Add(games[0]); // the user had already excluded one game library himself
        var h = new DefenderGameFolderExclusionsHandler(d, () => games);
        var a = Handler("defender.gameFolderExclusions");

        Assert.False(h.Read(a).Matches);
        var snap = h.Capture(a);
        h.Apply(a);
        Assert.Equal(3, d.Exclusions.Count);
        Assert.True(h.Read(a).Matches);

        h.Restore(a, snap);
        Assert.Equal(new[] { @"D:\MyOwnExclusion", games[0] }, d.Exclusions);
        Assert.True(h.Verify(a, snap));
    }

    [Fact]
    public void No_game_folder_means_not_applicable_and_unreadable_list_is_never_guessed()
    {
        var d = new FakeDefender();
        var a = Handler("defender.gameFolderExclusions");
        var none = new DefenderGameFolderExclusionsHandler(d, () => Array.Empty<string>());
        Assert.Throws<NotApplicableException>(() => none.Apply(a));

        var noRights = new DefenderGameFolderExclusionsHandler(new FakeDefender { Admin = false }, () => GameFolders(1));
        Assert.Throws<UnauthorizedAccessException>(() => noRights.Capture(a));
        Assert.False(noRights.Read(a).Matches);
    }
}
