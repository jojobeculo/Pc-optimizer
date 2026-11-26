using System.Globalization;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BiosManager;

public sealed class BiosUpdateGuidance
{
    public string? CurrentVersion { get; set; }
    public DateTime? CurrentDate { get; set; }
    public int? AgeDays { get; set; }
    public string? OfficialUrl { get; set; }
    public string? VendorTool { get; set; }
    public List<string> Steps { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public string Note { get; set; } = "";
}

/// <summary>
/// Motherboard / firmware analysis. FrameCore never downloads or flashes a BIOS: it shows the installed version,
/// links to the manufacturer's official support page for the exact model and gives the vendor-specific procedure.
/// There is no public API to query the latest BIOS version, so "update available" is never claimed.
/// </summary>
public static class FirmwareAdvisor
{
    public static string VendorKey(BoardInfo b)
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

    public static BiosUpdateGuidance UpdateGuidance(MachineProfile p, string? officialUrl, string? vendorTool)
    {
        var g = new BiosUpdateGuidance
        {
            CurrentVersion = p.Firmware.Version,
            CurrentDate = p.Firmware.ReleaseDate,
            AgeDays = p.Firmware.ReleaseDate is { } d ? (int)(DateTime.Today - d.Date).TotalDays : null,
            OfficialUrl = officialUrl,
            VendorTool = vendorTool,
            Note = Loc.T("bios.update.note"),
        };
        var key = VendorKey(p.Board);
        g.Steps.AddRange(Loc.T("bios.update.steps." + key).Split('|', StringSplitOptions.RemoveEmptyEntries));
        g.Risks.AddRange(Loc.T("bios.update.risks").Split('|', StringSplitOptions.RemoveEmptyEntries));
        return g;
    }

    public static List<Finding> Analyze(MachineProfile p, string? officialUrl)
    {
        var f = new List<Finding>();
        var key = VendorKey(p.Board);
        var fw = p.Firmware;
        if (fw.ReleaseDate is { } date && (DateTime.Today - date).TotalDays > 730)
            f.Add(new Finding
            {
                Id = "bios.old", Category = "Bios", Severity = Severity.Suggestion, Confidence = Confidence.Probable,
                Title = Loc.T("bios.old.title"), Detail = Loc.T("bios.old.detail", fw.Version, date.ToString("d", CultureInfo.CurrentCulture)),
                Recommendation = Loc.T("bios.old.reco"), Links = officialUrl != null ? new() { new LinkInfo(Loc.T("bios.officialPage"), officialUrl) } : new(),
                ActionId = "nav:bios",
            });
        if (fw.Mode == FirmwareMode.Legacy)
            f.Add(new Finding { Id = "bios.legacy", Category = "Bios", Severity = Severity.Warning, Title = Loc.T("bios.legacy.title"), Detail = Loc.T("bios.legacy.detail") });
        if (fw.SecureBoot == TriState.No)
            f.Add(new Finding { Id = "bios.secureboot", Category = "Bios", Severity = Severity.Info, Title = Loc.T("bios.secureboot.title"), Detail = Loc.T("bios.secureboot.detail") });
        if (fw.TpmPresent == false)
            f.Add(new Finding { Id = "bios.tpm", Category = "Bios", Severity = Severity.Info, Title = Loc.T("bios.tpm.title"), Detail = Loc.T("bios.tpm.detail") });

        foreach (var g in p.Gpus.Where(g => !g.IsIntegrated && g.ResizableBar == ResizableBarState.Disabled
                                            && FrameCore.HardwareDetection.Identification.ResizableBarCapableGpu(g.Vendor, g.Name)))
            f.Add(new Finding
            {
                Id = "bios.rebar", Category = "Bios", Severity = Severity.Suggestion, Confidence = Confidence.Probable,
                Title = Loc.T("bios.rebar.title"), Detail = Loc.T("bios.rebar.detail", g.Name, g.ResizableBarDetail),
                Steps = Loc.T("bios.steps.rebar." + key).Split('|').ToList(), Recommendation = Loc.T("bios.rebar.reco"),
            });

        var mem = p.Memory;
        if (mem.Xmp == XmpState.LikelyDisabled)
            f.Add(new Finding
            {
                Id = "ram.xmp", Category = "Ram", Severity = Severity.Warning, Confidence = Confidence.Probable,
                Title = Loc.T(mem.Type == "DDR5" && p.Cpu.Vendor == CpuVendor.Amd ? "ram.expo.title" : "ram.xmp.title"),
                Detail = mem.XmpReasoning ?? "", Steps = Loc.T("bios.steps.xmp." + key).Split('|').ToList(), Recommendation = Loc.T("ram.xmp.reco"),
            });
        else if (mem.Xmp == XmpState.Unknown && mem.XmpReasoning != null && mem.XmpReasoning.Contains("JEDEC", StringComparison.OrdinalIgnoreCase))
            f.Add(new Finding
            {
                Id = "ram.xmp.unknown", Category = "Ram", Severity = Severity.Info, Confidence = Confidence.Experimental,
                Title = Loc.T("ram.xmpUnknown.title"), Detail = mem.XmpReasoning, Steps = Loc.T("bios.steps.xmp." + key).Split('|').ToList(),
            });
        return f;
    }
}
