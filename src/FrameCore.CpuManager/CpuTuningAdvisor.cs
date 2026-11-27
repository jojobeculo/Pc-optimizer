using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.CpuManager;

public sealed class TuningTool
{
    public string Name { get; set; } = "";
    public bool Installed { get; set; }
    public string Url { get; set; } = "";
}

public sealed class CpuTuningReport
{
    public string Adapter { get; set; } = "";
    /// <summary>Whether this CPU/platform can be overclocked at all (by the vendor's means).</summary>
    public TriState OverclockSupported { get; set; }
    /// <summary>FrameCore never writes CPU tuning registers: no public vendor API exists.</summary>
    public bool ControllableFromFrameCore { get; set; }
    public string Summary { get; set; } = "";
    public List<string> BiosSteps { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<TuningTool> Tools { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
}

/// <summary>Vendor adapter contract: new vendors plug in without touching the rest of the application.</summary>
public interface ICpuTuningAdapter
{
    string Name { get; }
    bool Supports(CpuInfo cpu);
    CpuTuningReport Analyze(MachineProfile profile);
}

public static class CpuTuningAdvisor
{
    public static readonly List<ICpuTuningAdapter> Adapters = new() { new AmdRyzenAdapter(), new IntelCoreAdapter() };

    public static CpuTuningReport Analyze(MachineProfile p, IRegistryAccess? reg = null)
    {
        // In a virtual machine the hypervisor owns the processor: no BIOS, no vendor tool applies.
        if (p.Board.IsVirtualMachine)
            return new CpuTuningReport { Adapter = Loc.T("hw.vm"), OverclockSupported = TriState.No, Summary = Loc.T("cpu.tuning.virtualMachine") };
        var adapter = Adapters.FirstOrDefault(a => a.Supports(p.Cpu));
        var report = adapter?.Analyze(p) ?? new CpuTuningReport
        {
            Adapter = Loc.T("cpu.tuning.generic"), OverclockSupported = TriState.Unknown, Summary = Loc.T("cpu.tuning.unknownVendor"),
        };
        if (reg != null && p.Cpu.Vendor == CpuVendor.Intel) IntelCoreAdapter.CheckRaptorLakeMicrocode(p, reg, report);
        return report;
    }

    public static string BiosVendorKey(BoardInfo b)
    {
        var m = (b.Manufacturer ?? b.SystemManufacturer ?? "").ToUpperInvariant();
        if (m.Contains("ASUS")) return "asus";
        if (m.Contains("MICRO-STAR") || m.Contains("MSI")) return "msi";
        if (m.Contains("GIGABYTE")) return "gigabyte";
        if (m.Contains("ASROCK")) return "asrock";
        if (m.Contains("DELL") || m.Contains("ALIENWARE")) return "dell";
        if (m.Contains("HP") || m.Contains("HEWLETT")) return "hp";
        if (m.Contains("LENOVO")) return "lenovo";
        return "generic";
    }
}

public sealed class AmdRyzenAdapter : ICpuTuningAdapter
{
    public string Name => "AMD Ryzen";
    public bool Supports(CpuInfo cpu) => cpu.Vendor == CpuVendor.Amd;

    public CpuTuningReport Analyze(MachineProfile p)
    {
        var cpu = p.Cpu;
        var r = new CpuTuningReport { Adapter = Name, ControllableFromFrameCore = false };
        r.Tools.Add(new TuningTool { Name = "AMD Ryzen Master", Installed = p.InstalledVendorTools.Contains("AMD Ryzen Master"), Url = "https://www.amd.com/en/products/software/ryzen-master.html" });
        if (cpu.IsMobile || p.Board.IsLaptop)
        {
            r.OverclockSupported = TriState.No;
            r.Summary = Loc.T("cpu.amd.mobile");
            return r;
        }
        r.OverclockSupported = cpu.Name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ? TriState.Yes : TriState.Unknown;
        r.Summary = Loc.T("cpu.amd.desktop");
        var zen3Plus = cpu.Family >= 0x19;
        r.Notes.Add(Loc.T("cpu.amd.pbo"));
        if (zen3Plus) r.Notes.Add(Loc.T("cpu.amd.co"));
        if (cpu.IsX3D)
        {
            r.Notes.Add(cpu.Family >= 0x1A ? Loc.T("cpu.amd.x3d.zen5") : Loc.T("cpu.amd.x3d.limited"));
            if (cpu.IsDualCcdX3D) r.Notes.Add(Loc.T("cpu.amd.x3d.dualccd"));
        }
        r.BiosSteps.AddRange(Loc.T("bios.steps.pbo." + CpuTuningAdvisor.BiosVendorKey(p.Board)).Split('|'));
        r.Notes.Add(Loc.T("cpu.noVoltage"));
        return r;
    }
}

public sealed class IntelCoreAdapter : ICpuTuningAdapter
{
    public string Name => "Intel Core";
    public bool Supports(CpuInfo cpu) => cpu.Vendor == CpuVendor.Intel;

    public CpuTuningReport Analyze(MachineProfile p)
    {
        var cpu = p.Cpu;
        var r = new CpuTuningReport { Adapter = Name, ControllableFromFrameCore = false };
        var isUltra = cpu.Name.Contains("Ultra", StringComparison.OrdinalIgnoreCase);
        r.Tools.Add(new TuningTool
        {
            Name = isUltra ? "Intel XTU 10.x (+ Intel IPF)" : "Intel XTU 7.14",
            Installed = p.InstalledVendorTools.Contains("Intel XTU"),
            Url = "https://www.intel.com/content/www/us/en/download/17881/intel-extreme-tuning-utility-intel-xtu.html",
        });
        var chipset = p.Board.Chipset ?? "";
        var zChipset = chipset.StartsWith("Z", StringComparison.OrdinalIgnoreCase) || chipset.StartsWith("W", StringComparison.OrdinalIgnoreCase);
        if (cpu.Unlocked == TriState.Yes && !p.Board.IsLaptop)
        {
            r.OverclockSupported = string.IsNullOrEmpty(chipset) ? TriState.Unknown : zChipset ? TriState.Yes : TriState.No;
            r.Summary = r.OverclockSupported switch
            {
                TriState.Yes => Loc.T("cpu.intel.unlockedZ", chipset),
                TriState.No => Loc.T("cpu.intel.unlockedNonZ", chipset),
                _ => Loc.T("cpu.intel.unlockedUnknownChipset"),
            };
        }
        else if (cpu.Unlocked == TriState.Unknown)
        {
            r.OverclockSupported = TriState.Unknown;
            r.Summary = Loc.T("cpu.intel.hx");
        }
        else
        {
            r.OverclockSupported = TriState.No;
            r.Summary = Loc.T("cpu.intel.locked");
        }
        if (chipset.StartsWith("B", StringComparison.OrdinalIgnoreCase) && cpu.Family == 6 && cpu.Model >= 0x97)
            r.Notes.Add(Loc.T("cpu.intel.bMemoryOc"));
        r.Notes.Add(Loc.T("cpu.intel.powerLimits"));
        r.Notes.Add(Loc.T("cpu.noVoltage"));
        r.BiosSteps.AddRange(Loc.T("bios.steps.intel." + CpuTuningAdvisor.BiosVendorKey(p.Board)).Split('|'));
        return r;
    }

    /// <summary>
    /// 13th/14th gen desktop Core i7/i9 (Raptor Lake, family 6 model 0xB7/0xBF): Intel's Vmin-shift instability fix
    /// ships as microcode 0x12B or later through BIOS updates. The installed revision is read from the registry.
    /// </summary>
    public static void CheckRaptorLakeMicrocode(MachineProfile p, IRegistryAccess reg, CpuTuningReport report)
    {
        var cpu = p.Cpu;
        if (cpu.Family != 6 || cpu.Model is not (0xB7 or 0xBF) || cpu.IsMobile) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(cpu.Name, @"i[79]-1[34]\d{3}")) return;
        var rev = MicrocodeRevision(reg);
        var f = new Finding
        {
            Id = "cpu.raptorlake.microcode", Category = "Cpu", Confidence = Confidence.Confirmed,
            Links = { new LinkInfo("Intel", "https://community.intel.com/t5/Mobile-and-Desktop-Processors/bd-p/mobile-and-desktop-processors") },
        };
        if (rev == null)
        {
            f.Severity = Severity.Warning;
            f.Title = Loc.T("cpu.rpl.title");
            f.Detail = Loc.T("cpu.rpl.unknown");
        }
        else if (rev < 0x12B)
        {
            f.Severity = Severity.Critical;
            f.Title = Loc.T("cpu.rpl.title");
            f.Detail = Loc.T("cpu.rpl.old", $"0x{rev:X}");
        }
        else return;
        f.Recommendation = Loc.T("cpu.rpl.recommendation");
        report.Findings.Add(f);
    }

    /// <summary>HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0 "Update Revision" (high DWORD = revision).</summary>
    public static uint? MicrocodeRevision(IRegistryAccess reg)
    {
        var v = reg.GetValue(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "Update Revision");
        if (v?.Data is byte[] b && b.Length >= 8) return BitConverter.ToUInt32(b, 4);
        return null;
    }
}
