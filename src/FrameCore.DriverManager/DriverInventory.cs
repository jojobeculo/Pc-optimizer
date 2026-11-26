using System.Globalization;
using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.DriverManager;

public sealed class DriverEntry
{
    public string DeviceName { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string? DeviceClass { get; set; }
    public string? Manufacturer { get; set; }
    public string? Provider { get; set; }
    public string? Version { get; set; }
    public DateTime? Date { get; set; }
    public bool? Signed { get; set; }
    public string? Signer { get; set; }
    public string? InfName { get; set; }
    public string? HardwareId { get; set; }
    public string? DeviceId { get; set; }
    public string? OfficialSource { get; set; }
    public string? OfficialUrl { get; set; }
    public int? AgeDays => Date is { } d ? (int)(DateTime.Today - d.Date).TotalDays : null;
}

public static class DriverInventory
{
    public static readonly string[] Categories = { "Gpu", "Chipset", "Audio", "Ethernet", "WiFi", "Bluetooth", "Usb", "Storage", "Input", "Other" };

    public static List<DriverEntry> Scan(MachineProfile? profile = null)
    {
        var list = new List<DriverEntry>();
        foreach (var r in Wmi.Query("SELECT DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, IsSigned, Signer, InfName, HardWareID, DeviceID FROM Win32_PnPSignedDriver"))
        {
            var name = r.Str("DeviceName");
            if (string.IsNullOrWhiteSpace(name) || r.Str("DriverVersion") == null) continue;
            var e = new DriverEntry
            {
                DeviceName = name, DeviceClass = r.Str("DeviceClass"), Manufacturer = r.Str("Manufacturer"), Provider = r.Str("DriverProviderName"),
                Version = r.Str("DriverVersion"), Date = r.Date("DriverDate"), Signed = r.Bool("IsSigned"), Signer = r.Str("Signer"),
                InfName = r.Str("InfName"), HardwareId = r.Str("HardWareID"), DeviceId = r.Str("DeviceID"),
            };
            e.Category = Categorize(e);
            (e.OfficialSource, e.OfficialUrl) = OfficialSource(e, profile);
            list.Add(e);
        }
        // Keep one row per device+driver (WMI may return duplicates for composite devices).
        return list.GroupBy(d => (d.DeviceName, d.Version, d.DeviceId)).Select(g => g.First())
                   .OrderBy(d => Array.IndexOf(Categories, d.Category)).ThenBy(d => d.DeviceName).ToList();
    }

    public static string Categorize(DriverEntry e)
    {
        var cls = (e.DeviceClass ?? "").ToUpperInvariant();
        var n = e.DeviceName.ToUpperInvariant();
        var hw = (e.HardwareId ?? "").ToUpperInvariant();
        if (cls == "DISPLAY") return "Gpu";
        if (cls == "NET")
        {
            if (n.Contains("WI-FI") || n.Contains("WIFI") || n.Contains("WIRELESS") || n.Contains("802.11") || n.Contains("WLAN")) return "WiFi";
            if (n.Contains("VIRTUAL") || n.Contains("WAN MINIPORT") || n.Contains("BLUETOOTH")) return "Other";
            return "Ethernet";
        }
        if (cls == "BLUETOOTH") return "Bluetooth";
        if (cls is "MEDIA" && !n.Contains("CAMERA")) return "Audio";
        if (cls is "USB") return "Usb";
        if (cls is "SCSIADAPTER" or "HDC" or "DISKDRIVE") return "Storage";
        if (cls is "HIDCLASS" or "MOUSE" or "KEYBOARD") return "Input";
        if (cls == "SYSTEM" && (n.Contains("CHIPSET") || n.Contains("SMBUS") || n.Contains("MANAGEMENT ENGINE") || n.Contains("PCI EXPRESS ROOT") || n.Contains("GPIO")
                                || n.Contains("PSP") || n.Contains("3D V-CACHE") || n.Contains("PPM") || n.Contains("SERIAL IO") || hw.Contains("VEN_1022") || hw.Contains("VEN_8086")))
            return "Chipset";
        return "Other";
    }

    /// <summary>Official download source by vendor. Links only; nothing is downloaded from third-party sites.</summary>
    public static (string? source, string? url) OfficialSource(DriverEntry e, MachineProfile? p)
    {
        var who = ((e.Provider ?? "") + " " + (e.Manufacturer ?? "") + " " + e.DeviceName).ToUpperInvariant();
        if (who.Contains("NVIDIA")) return ("NVIDIA", "https://www.nvidia.com/en-us/drivers/");
        if (who.Contains("AMD") || who.Contains("ADVANCED MICRO")) return e.Category == "Chipset"
            ? ("AMD", "https://www.amd.com/en/support/download/drivers.html")
            : ("AMD", "https://www.amd.com/en/support/download/drivers.html");
        if (who.Contains("INTEL")) return ("Intel", "https://www.intel.com/content/www/us/en/support/detect.html");
        if (who.Contains("REALTEK") || who.Contains("MEDIATEK") || who.Contains("QUALCOMM") || who.Contains("KILLER") || who.Contains("CONEXANT"))
            return (Loc.T("drivers.source.board"), p != null ? BoardSupport.SupportUrl(p.Board) : null);
        if (who.Contains("MICROSOFT")) return ("Windows Update", "ms-settings:windowsupdate");
        return (Loc.T("drivers.source.windowsUpdate"), "ms-settings:windowsupdate");
    }

    public static List<Finding> Analyze(IReadOnlyList<DriverEntry> drivers, MachineProfile p)
    {
        var findings = new List<Finding>();
        foreach (var gpu in drivers.Where(d => d.Category == "Gpu"))
        {
            if (gpu.DeviceName.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || gpu.DeviceName.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                findings.Add(new Finding
                {
                    Id = "drivers.gpu.basic", Category = "Drivers", Severity = Severity.Critical, Title = Loc.T("drivers.basic.title"),
                    Detail = Loc.T("drivers.basic.detail"), Recommendation = Loc.T("drivers.basic.reco"),
                });
            else if (gpu.AgeDays > 270)
                findings.Add(new Finding
                {
                    Id = "drivers.gpu.old." + gpu.DeviceName, Category = "Drivers", Severity = Severity.Suggestion, Confidence = Confidence.Probable,
                    Title = Loc.T("drivers.gpuOld.title", gpu.DeviceName), Detail = Loc.T("drivers.gpuOld.detail", gpu.Version, gpu.Date?.ToString("d", CultureInfo.CurrentCulture), gpu.AgeDays),
                    Recommendation = Loc.T("drivers.gpuOld.reco"), Links = { new LinkInfo(gpu.OfficialSource ?? "", gpu.OfficialUrl ?? "") },
                });
        }
        foreach (var d in drivers.Where(d => d.Signed == false))
            findings.Add(new Finding
            {
                Id = "drivers.unsigned." + d.DeviceName, Category = "Drivers", Severity = Severity.Warning,
                Title = Loc.T("drivers.unsigned.title", d.DeviceName), Detail = Loc.T("drivers.unsigned.detail", d.Provider, d.InfName),
            });
        if (p.Cpu.Vendor == CpuVendor.Amd && !p.Board.IsLaptop && !p.InstalledVendorTools.Contains("AMD Chipset Software"))
            findings.Add(new Finding
            {
                Id = "drivers.amd.chipset", Category = "Drivers", Severity = p.Cpu.IsDualCcdX3D ? Severity.Warning : Severity.Suggestion,
                Confidence = Confidence.Probable, Title = Loc.T("drivers.amdChipset.title"),
                Detail = p.Cpu.IsDualCcdX3D ? Loc.T("drivers.amdChipset.x3d") : Loc.T("drivers.amdChipset.detail"),
                Links = { new LinkInfo("AMD", "https://www.amd.com/en/support/download/drivers.html") },
            });
        return findings;
    }
}

/// <summary>Official support pages of motherboard / PC manufacturers (search by model when the site supports it).</summary>
public static class BoardSupport
{
    public static string? SupportUrl(BoardInfo b)
    {
        var maker = (b.Manufacturer ?? b.SystemManufacturer ?? "").ToUpperInvariant();
        var model = Uri.EscapeDataString(b.Product ?? b.SystemModel ?? "");
        if (maker.Contains("ASUS")) return $"https://www.asus.com/searchresult?searchType=support&searchKey={model}";
        if (maker.Contains("MICRO-STAR") || maker.Contains("MSI")) return $"https://www.msi.com/search/{model}";
        if (maker.Contains("GIGABYTE")) return $"https://www.gigabyte.com/Search?kw={model}";
        if (maker.Contains("ASROCK")) return "https://www.asrock.com/support/index.asp";
        if (maker.Contains("LENOVO")) return "https://pcsupport.lenovo.com/";
        if (maker.Contains("HP") || maker.Contains("HEWLETT")) return "https://support.hp.com/drivers";
        if (maker.Contains("DELL") || maker.Contains("ALIENWARE")) return "https://www.dell.com/support/home";
        if (maker.Contains("ACER")) return "https://www.acer.com/support";
        if (maker.Contains("BIOSTAR")) return "https://www.biostar.com.tw/app/en/support/";
        if (maker.Contains("MICROSOFT")) return "https://support.microsoft.com/surface";
        return null;
    }

    public static string? VendorUpdateTool(BoardInfo b)
    {
        var maker = (b.SystemManufacturer ?? b.Manufacturer ?? "").ToUpperInvariant();
        if (maker.Contains("LENOVO")) return "Lenovo Vantage / Lenovo System Update";
        if (maker.Contains("HP") || maker.Contains("HEWLETT")) return "HP Support Assistant";
        if (maker.Contains("DELL") || maker.Contains("ALIENWARE")) return "Dell Command | Update / SupportAssist";
        return null;
    }
}
