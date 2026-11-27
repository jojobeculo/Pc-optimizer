using FrameCore.Core.Hardware;
using FrameCore.Core.Models;
using FrameCore.CpuManager;
using Xunit;

namespace FrameCore.Tests;

public class CpuOcGuideTests
{
    private static readonly CpuOcGuide Guide = CpuOcGuide.Load(Path.Combine(Repo.Catalog, "knowledge"));

    [Theory]
    [InlineData(CpuVendor.Amd, "AMD Ryzen 5 7600 6-Core Processor", false, "am5")]
    [InlineData(CpuVendor.Amd, "AMD Ryzen 5 9600X 6-Core Processor", false, "am5")]
    [InlineData(CpuVendor.Amd, "AMD Ryzen 7 9800X3D 8-Core Processor", true, "am5x3d")]
    [InlineData(CpuVendor.Amd, "AMD Ryzen 7 5800X3D 8-Core Processor", true, "am4")]
    [InlineData(CpuVendor.Amd, "AMD Ryzen 5 5600X 6-Core Processor", false, "am4")]
    [InlineData(CpuVendor.Amd, "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", false, null)]
    [InlineData(CpuVendor.Intel, "Intel(R) Core(TM) i7-14700K", false, "intelk")]
    [InlineData(CpuVendor.Intel, "12th Gen Intel(R) Core(TM) i5-12400F", false, "intellocked")]
    [InlineData(CpuVendor.Intel, "13th Gen Intel(R) Core(TM) i7-13700H", false, null)]
    [InlineData(CpuVendor.Intel, "Intel(R) Core(TM) Ultra 7 265K", false, "intelultra")]
    [InlineData(CpuVendor.Amd, "AMD EPYC 7763 64-Core Processor", false, null)]
    public void Family_is_recognised_and_never_guessed(CpuVendor vendor, string name, bool x3d, string? expected)
    {
        var cpu = new CpuInfo { Vendor = vendor, Name = name, IsX3D = x3d };
        Assert.Equal(expected, CpuOcGuide.FamilyId(cpu, new BoardInfo()));
    }

    [Fact]
    public void Laptops_and_virtual_machines_get_no_values()
    {
        var cpu = new CpuInfo { Vendor = CpuVendor.Amd, Name = "AMD Ryzen 5 7600 6-Core Processor" };
        Assert.Null(CpuOcGuide.FamilyId(cpu, new BoardInfo { IsLaptop = true }));
        Assert.Null(CpuOcGuide.FamilyId(cpu, new BoardInfo { SystemManufacturer = "Microsoft Corporation", SystemModel = "Virtual Machine" }));
    }

    [Fact]
    public void Every_family_has_three_levels_with_settings_located_in_every_brand_bios_in_both_languages()
    {
        Assert.Equal(4, Guide.Brands.Count);
        Assert.False(string.IsNullOrWhiteSpace(Guide.Disclaimer.Fr));
        foreach (var f in Guide.Families)
        {
            foreach (var level in new[] { "Light", "Moderate", "High" })
            {
                Assert.True(f.Levels.ContainsKey(level), $"{f.Id} {level}");
                Assert.NotEmpty(f.Levels[level]);
                Assert.True(f.Gain.ContainsKey(level), $"{f.Id} gain {level}");
                foreach (var ls in f.Levels[level])
                {
                    Assert.True(Guide.Settings.ContainsKey(ls.Setting), $"{f.Id}: unknown setting {ls.Setting}");
                    Assert.False(string.IsNullOrWhiteSpace(ls.Value.Fr) || string.IsNullOrWhiteSpace(ls.Value.En), $"{f.Id} {ls.Setting}");
                    foreach (var b in Guide.Brands)
                    {
                        Assert.True(Guide.Settings[ls.Setting].Paths.TryGetValue(b.Id, out var p), $"{ls.Setting} has no path for {b.Id}");
                        Assert.Contains(p!.Tab, b.Tabs);
                    }
                }
            }
            // Each level builds on the previous one.
            Assert.True(f.Levels["Light"].Count <= f.Levels["Moderate"].Count && f.Levels["Moderate"].Count <= f.Levels["High"].Count, f.Id);
        }
        foreach (var s in Guide.Settings.Values) Assert.False(string.IsNullOrWhiteSpace(s.Why.En) || string.IsNullOrWhiteSpace(s.Label.Fr));
    }

    [Fact]
    public void No_level_suggests_a_dangerous_voltage()
    {
        // AMD SoC voltage capped at 1.30 V; Intel 13th/14th gen never above 1.40 V; no level raises a core voltage.
        var all = Guide.Families.SelectMany(f => f.Levels.Values.SelectMany(l => l)).ToList();
        Assert.DoesNotContain(all, l => l.Setting == "vcoreOffset" && !l.Value.En.Contains('−'));
        Assert.Contains(Guide.Family("am5")!.Advanced, e => e.Value.En.Contains("1.30 V"));
        Assert.Contains(Guide.Family("intelk")!.Advanced, e => e.Value.En.Contains("1.40 V"));
    }
}
