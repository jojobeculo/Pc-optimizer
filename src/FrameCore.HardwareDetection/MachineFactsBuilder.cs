using FrameCore.Core.Catalog;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.HardwareDetection;

/// <summary>Converts a <see cref="MachineProfile"/> into the flat facts used by catalog conditions.</summary>
public static class MachineFactsBuilder
{
    /// <param name="powerSchemes">GUIDs of existing power plans (null = unknown).</param>
    /// <param name="physicalPrinters">Number of non-virtual printers (null = unknown).</param>
    public static MachineFacts Build(MachineProfile p, IReadOnlyCollection<Guid>? powerSchemes = null, int? physicalPrinters = null)
    {
        var f = new MachineFacts();
        f["os.build"] = p.Os.Build;
        f["os.isWindows11"] = p.Os.IsWindows11;
        f["os.edition"] = p.Os.EditionId;
        f["os.displayVersion"] = p.Os.DisplayVersion;

        f["cpu.vendor"] = p.Cpu.Vendor switch { CpuVendor.Intel => "intel", CpuVendor.Amd => "amd", _ => "other" };
        f["cpu.isHybrid"] = p.Cpu.IsHybrid;
        f["cpu.isX3D"] = p.Cpu.IsX3D;
        f["cpu.isDualCcdX3D"] = p.Cpu.IsDualCcdX3D;
        f["cpu.cores"] = p.Cpu.Cores;
        f["cpu.threads"] = p.Cpu.Threads;
        f["cpu.isMobile"] = p.Cpu.IsMobile;
        f["cpu.unlocked"] = p.Cpu.Unlocked == TriState.Yes;

        var gpus = p.Gpus.Where(g => !g.IsSoftware).ToList();
        f["gpu.hasNvidia"] = gpus.Any(g => g.Vendor == GpuVendor.Nvidia);
        f["gpu.hasAmdDiscrete"] = gpus.Any(g => g.Vendor == GpuVendor.Amd && !g.IsIntegrated);
        f["gpu.hasIntelArc"] = gpus.Any(g => g.Vendor == GpuVendor.Intel && !g.IsIntegrated);
        f["gpu.hasDiscrete"] = gpus.Any(g => !g.IsIntegrated);
        f["gpu.count"] = gpus.Count;
        f["gpu.isHybridLaptop"] = p.Board.IsLaptop && gpus.Any(g => g.IsIntegrated) && gpus.Any(g => !g.IsIntegrated);
        if (gpus.Any(g => g.HagsSupported != null)) f["gpu.hagsSupported"] = gpus.Any(g => g.HagsSupported == true);
        if (gpus.Any(g => g.HagsEnabled != null)) f["gpu.hagsEnabled"] = gpus.Any(g => g.HagsEnabled == true);

        f["system.isLaptop"] = p.Board.IsLaptop;
        f["system.isDesktop"] = !p.Board.IsLaptop;
        f["system.hasBattery"] = p.Board.HasBattery;
        f["system.modernStandby"] = p.Firmware.ModernStandby;

        if (powerSchemes != null)
        {
            f["power.highPerformanceAvailable"] = powerSchemes.Contains(PowerGuidsShim.HighPerformance);
            f["power.ultimateAvailable"] = powerSchemes.Contains(PowerGuidsShim.Ultimate);
        }

        f["ram.totalGb"] = Math.Round(p.Memory.TotalBytes / 1073741824.0, 1);
        f["ram.type"] = p.Memory.Type;

        var sys = p.Storage.FirstOrDefault(s => s.IsSystemDisk);
        if (sys != null) f["storage.systemIsSsd"] = sys.Media == MediaKind.Ssd;
        f["storage.hasHdd"] = p.Storage.Any(s => s.Media == MediaKind.Hdd);
        f["storage.hasSsd"] = p.Storage.Any(s => s.Media == MediaKind.Ssd);

        f["display.vrrCapable"] = p.Displays.Any(d => d.AdaptiveSyncAdvertised == true || d.FreeSyncAdvertised == true || d.HdmiVrrAdvertised == true);
        f["display.hdrCapable"] = p.Displays.Any(d => d.HdrSupported == true);
        f["display.count"] = p.Displays.Count;

        f["net.hasEthernet"] = p.Network.Any(n => n.IsPhysical && !n.IsWireless && n.Type == "Ethernet");
        f["net.activeEthernet"] = p.Network.Any(n => n.IsPhysical && !n.IsWireless && n.Type == "Ethernet" && n.IsUp);
        f["net.hasWifi"] = p.Network.Any(n => n.IsPhysical && n.IsWireless);
        f["system.isVirtualMachine"] = p.Board.IsVirtualMachine;

        f["security.vbs"] = p.Security.VbsRunning == TriState.Yes;
        f["security.hvci"] = p.Security.HvciRunning == TriState.Yes;
        if (p.Security.VirtualMachinePlatformEnabled != TriState.Unknown) f["security.vmpEnabled"] = p.Security.VirtualMachinePlatformEnabled == TriState.Yes;

        if (physicalPrinters != null) f["printers.physicalCount"] = physicalPrinters.Value;
        f["tools.installed"] = p.InstalledVendorTools;
        return f;
    }

    /// <summary>Non-virtual printers (PDF/XPS/OneNote/Fax queues are ignored).</summary>
    public static int? CountPhysicalPrinters()
    {
        var rows = Wmi.Query("SELECT Name, PortName FROM Win32_Printer");
        if (!OperatingSystem.IsWindows()) return null;
        string[] virtualMarkers = { "PDF", "XPS", "OneNote", "Fax", "Send To", "Microsoft Print", "Snagit", "Adobe" };
        return rows.Count(r => !virtualMarkers.Any(v => (r.Str("Name") ?? "").Contains(v, StringComparison.OrdinalIgnoreCase))
                               && !(r.Str("PortName") ?? "").StartsWith("PORTPROMPT", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Power plan GUIDs needed by facts without depending on the WindowsOptimization module.</summary>
public static class PowerGuidsShim
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
}
