using System.Text.Json;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;

namespace FrameCore.CpuManager;

/// <summary>One motherboard brand: how to enter its BIOS and how its screens look (colours only, no logo).</summary>
public sealed class BiosBrand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public LocalizedText Enter { get; set; } = new();
    public LocalizedText Advanced { get; set; } = new();
    public LocalizedText Save { get; set; } = new();
    public List<string> Tabs { get; set; } = new();
    public string Accent { get; set; } = "#2F80ED";
    public string Bar { get; set; } = "#111111";
}

public sealed class BiosPath { public string Tab { get; set; } = ""; public List<string> Path { get; set; } = new(); }

public sealed class BiosSetting
{
    public LocalizedText Label { get; set; } = new();
    public LocalizedText Why { get; set; } = new();
    public Dictionary<string, BiosPath> Paths { get; set; } = new();
}

public sealed class LevelSetting { public string Setting { get; set; } = ""; public LocalizedText Value { get; set; } = new(); }
public sealed class ExpertSetting { public LocalizedText Name { get; set; } = new(); public LocalizedText Value { get; set; } = new(); public LocalizedText Why { get; set; } = new(); }

public sealed class CpuOcFamily
{
    public string Id { get; set; } = "";
    public LocalizedText Title { get; set; } = new();
    /// <summary>Light | Moderate | High → BIOS settings with their concrete values.</summary>
    public Dictionary<string, List<LevelSetting>> Levels { get; set; } = new();
    public Dictionary<string, LocalizedText> Gain { get; set; } = new();
    public List<ExpertSetting> Advanced { get; set; } = new();
    public List<LocalizedText> Notes { get; set; } = new();
}

public sealed class WindowsRow { public LocalizedText Label { get; set; } = new(); public LocalizedText Value { get; set; } = new(); }

/// <summary>A Windows screen to illustrate (Task Manager check, AMD Ryzen Master).</summary>
public sealed class WindowsIllustration
{
    public string Id { get; set; } = "";
    public LocalizedText Title { get; set; } = new();
    public LocalizedText Window { get; set; } = new();
    public List<LocalizedText> Path { get; set; } = new();
    public List<WindowsRow> Rows { get; set; } = new();
    public int Highlight { get; set; }
    public LocalizedText Why { get; set; } = new();
}

/// <summary>
/// CPU tuning guide (catalog/knowledge/cpu-oc-guide.json): per processor family, three levels of BIOS settings with
/// concrete values, expert settings, and where each option sits in the BIOS of each motherboard brand. Guidance only:
/// FrameCore never writes CPU tuning registers or BIOS settings.
/// </summary>
public sealed class CpuOcGuide
{
    public LocalizedText Disclaimer { get; set; } = new();
    public List<BiosBrand> Brands { get; set; } = new();
    public Dictionary<string, BiosSetting> Settings { get; set; } = new();
    public List<CpuOcFamily> Families { get; set; } = new();
    public List<WindowsIllustration> Windows { get; set; } = new();

    public static CpuOcGuide Load(string knowledgeDir)
    {
        var file = Path.Combine(knowledgeDir, "cpu-oc-guide.json");
        try { return File.Exists(file) ? JsonSerializer.Deserialize<CpuOcGuide>(File.ReadAllText(file), Json.Options) ?? new() : new(); }
        catch (Exception ex) { Log.Error("CpuOcGuide", "cpu-oc-guide.json unreadable", ex); return new(); }
    }

    /// <summary>Guide family of a desktop processor; null for laptops, virtual machines and unknown processors.</summary>
    public static string? FamilyId(CpuInfo cpu, BoardInfo board)
    {
        if (board.IsLaptop || board.IsVirtualMachine || string.IsNullOrWhiteSpace(cpu.Name)) return null;
        var name = cpu.Name;
        if (cpu.Vendor == CpuVendor.Amd)
        {
            var m = Regex.Match(name, @"Ryzen\s+(?:Threadripper\s+)?\d\s+(?:PRO\s+)?(\d)(\d{3})(\w*)", RegexOptions.IgnoreCase);
            if (!m.Success || name.Contains("Threadripper", StringComparison.OrdinalIgnoreCase)) return null;
            var suffix = m.Groups[3].Value.ToUpperInvariant();
            if (suffix.StartsWith("H") || suffix.StartsWith("U")) return null; // mobile chips
            var series = m.Groups[1].Value[0];
            var x3d = cpu.IsX3D || suffix.Contains("X3D");
            return series is '7' or '8' or '9' ? (x3d ? "am5x3d" : "am5") : series is '2' or '3' or '5' ? "am4" : null;
        }
        if (cpu.Vendor == CpuVendor.Intel)
        {
            if (Regex.IsMatch(name, @"Core\(?T?M?\)?\s*Ultra\s*\d\s*2\d{2}K", RegexOptions.IgnoreCase)) return "intelultra";
            var core = Regex.Match(name, @"i[3579]-1[234]\d{3}(\w*)", RegexOptions.IgnoreCase);
            if (core.Success)
            {
                var sfx = core.Groups[1].Value.ToUpperInvariant();
                if (sfx.StartsWith("H") || sfx.StartsWith("U") || sfx.StartsWith("P")) return null;
                return sfx.StartsWith("K") ? "intelk" : "intellocked";
            }
            if (Regex.IsMatch(name, @"Core\(?T?M?\)?\s*Ultra\s*\d\s*2\d{2}F?\b", RegexOptions.IgnoreCase)) return "intellocked";
        }
        return null;
    }

    public CpuOcFamily? Family(string? id) => id == null ? null : Families.FirstOrDefault(f => f.Id == id);
}
