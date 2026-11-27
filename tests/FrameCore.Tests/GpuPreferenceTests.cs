using FrameCore.Core.Models;
using FrameCore.GpuManager;
using Xunit;

namespace FrameCore.Tests;

public class GpuPreferenceTests
{
    [Theory]
    [InlineData("GpuPreference=2;", GpuPreference.HighPerformance)]
    [InlineData("SwapEffectUpgradeEnable=1;GpuPreference=1;", GpuPreference.PowerSaving)]
    [InlineData("GpuPreference=0;", GpuPreference.Auto)]
    public void Windows_data_format_is_parsed(string data, GpuPreference expected) => Assert.Equal(expected, GpuPreferences.Parse(data));

    [Theory]
    [InlineData("SwapEffectUpgradeEnable=1;")]
    [InlineData("GpuPreference=7;")]
    [InlineData(null)]
    public void Unknown_data_is_not_a_preference(string? data) => Assert.Null(GpuPreferences.Parse(data));

    [Theory]
    [InlineData(@"C:\Games\game.exe", true)]
    [InlineData(@"game.exe", false)]
    [InlineData(@"C:\Games\*.exe", false)]
    [InlineData(@"C:\Games\setup.msi", false)]
    public void Only_full_executable_paths_are_accepted(string path, bool ok)
    {
        // Path.IsPathFullyQualified follows the host OS; the Windows form is what matters here.
        if (!OperatingSystem.IsWindows() && ok) return;
        Assert.Equal(ok, GpuPreferences.IsValidExecutable(path));
    }

    [Fact]
    public void Preference_is_applied_journaled_read_back_and_restored_exactly()
    {
        if (!OperatingSystem.IsWindows()) { RoundTrip("/games/game.exe"); return; }
        RoundTrip(@"C:\Games\game.exe");
    }

    private static void RoundTrip(string exe)
    {
        var sim = new Sim();
        // Another application already has a preference, and the global DirectX value exists: both stay untouched.
        sim.Registry.SetValue(GpuPreferences.Key, @"C:\Other\tool.exe", "String", "GpuPreference=1;");
        sim.Registry.SetValue(GpuPreferences.Key, "DirectXUserGlobalSettings", "String", "SwapEffectUpgradeEnable=1;");
        var action = GpuPreferences.Action(exe, GpuPreference.HighPerformance);
        var r = sim.Engine.ApplyChangeSet(new ChangeSet { Origin = "gpuPreference", Category = "Gpu", Title = new LocalizedText("t", "t") },
                                          new[] { action }, new Core.Engine.ActionContext(null, "gpuPreference"));
        Assert.True(r.Success);
        Assert.True(r.Verified);
        Assert.Equal("GpuPreference=2;", sim.Registry.GetValue(GpuPreferences.Key, exe)?.Data);
        var listed = GpuPreferences.Read(sim.Registry);
        Assert.Equal(2, listed.Count); // the global settings value is not an application
        Assert.Contains(listed, p => p.ExePath == exe && p.Preference == GpuPreference.HighPerformance);

        Assert.True(sim.Engine.Restore(r.ChangeSet!).Success);
        Assert.Null(sim.Registry.GetValue(GpuPreferences.Key, exe));
        Assert.Equal("GpuPreference=1;", sim.Registry.GetValue(GpuPreferences.Key, @"C:\Other\tool.exe")?.Data);
    }
}
