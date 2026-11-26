using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.HardwareDetection;

/// <summary>
/// Builds the "data reliability" table of a profile: for each key value, where it comes from and how much it can be
/// trusted. Nothing here changes a value; it only documents the provenance so that the UI never presents an inferred
/// or missing value as a certainty.
/// </summary>
public static class DataQualityAssessor
{
    public static List<DataPoint> Assess(MachineProfile p)
    {
        var list = new List<DataPoint>();
        void Add(string area, string field, DataConfidence c, string source, string? note = null) =>
            list.Add(new DataPoint { Area = area, Field = field, Confidence = c, Source = source, Note = note });
        static DataConfidence Has(bool ok) => ok ? DataConfidence.Reported : DataConfidence.Unavailable;
        var vm = p.Board.IsVirtualMachine;
        var vmNote = vm ? Loc.T("dq.vmNote") : null;

        Add("cpu", Loc.T("hw.cpu.model"), Has(!string.IsNullOrWhiteSpace(p.Cpu.Name)), "CPUID");
        Add("cpu", Loc.T("hw.cpu.cores"), p.Cpu.Cores > 0 ? DataConfidence.Measured : DataConfidence.Unavailable, "GetLogicalProcessorInformationEx", vm ? Loc.T("dq.vmCpu") : null);
        Add("cpu", Loc.T("hw.cpu.base"), Has(p.Cpu.BaseMhz is > 0), "SMBIOS / WMI Win32_Processor");
        var pawn = p.InstalledVendorTools.Contains("PawnIO");
        Add("cpu", Loc.T("dq.cpuTemp"), pawn ? DataConfidence.Measured : DataConfidence.Unavailable, "LibreHardwareMonitor + PawnIO", pawn ? null : Loc.T("dq.cpuTempNote"));

        var g = p.PrimaryGpu;
        if (g != null)
        {
            Add("gpu", Loc.T("hw.gpu"), Has(!string.IsNullOrWhiteSpace(g.Name)), "DXGI", g.IsSoftware ? Loc.T("dq.softwareGpu") : null);
            Add("gpu", Loc.T("hw.gpu.vram"), Has(g.DedicatedVramBytes is > 0), "DXGI_ADAPTER_DESC1");
            Add("gpu", Loc.T("hw.gpu.driver"), Has(g.DriverVersion != null), "Registry / WMI Win32_VideoController");
            Add("gpu", Loc.T("hw.gpu.hags"), Has(g.HagsSupported != null), "D3DKMTQueryAdapterInfo");
            Add("gpu", Loc.T("hw.gpu.rebar"), g.ResizableBar switch
            {
                ResizableBarState.Unknown => DataConfidence.Unavailable,
                _ when g.Vendor == GpuVendor.Nvidia => DataConfidence.Reported,
                _ => DataConfidence.Inferred,
            }, g.Vendor == GpuVendor.Nvidia ? "NVML BAR1" : "PCI memory window size", g.ResizableBarDetail);
            Add("gpu", Loc.T("hw.gpu.pcie"), Has(g.PcieCurrentGen != null), "NVML / ADLX");
        }
        else Add("gpu", Loc.T("hw.gpu"), DataConfidence.Unavailable, "DXGI");

        var m = p.Memory;
        Add("ram", Loc.T("hw.ram.total"), m.TotalBytes > 0 ? DataConfidence.Measured : DataConfidence.Unavailable, "GlobalMemoryStatusEx");
        Add("ram", Loc.T("hw.ram.speed"), Has(m.ConfiguredSpeedMts != null), "SMBIOS (Win32_PhysicalMemory.ConfiguredClockSpeed)", vmNote);
        Add("ram", Loc.T("hw.ram.channels"), m.ChannelsConfidence, Loc.T("dq.channelsSource"), m.ChannelReasoning);
        Add("ram", Loc.T("hw.ram.xmp"), m.Xmp is XmpState.LikelyEnabled or XmpState.LikelyDisabled ? DataConfidence.Inferred : DataConfidence.Unavailable,
            Loc.T("dq.xmpSource"), m.XmpReasoning);
        Add("ram", Loc.T("hw.ram.timings"), DataConfidence.Unavailable, "SPD", Loc.T("hw.ram.timingsNote"));

        Add("board", Loc.T("hw.board.model"), Has(!string.IsNullOrWhiteSpace(p.Board.Product) && !IsPlaceholder(p.Board.Product)), "SMBIOS (Win32_BaseBoard)", vmNote);
        Add("board", Loc.T("hw.board.chipset"), p.Board.Chipset != null ? DataConfidence.Inferred : DataConfidence.Unavailable, Loc.T("dq.chipsetSource"));
        Add("board", Loc.T("hw.bios.version"), Has(p.Firmware.Version != null), "SMBIOS (Win32_BIOS)");
        Add("board", Loc.T("hw.bios.secureboot"), Has(p.Firmware.SecureBoot != TriState.Unknown), "GetFirmwareEnvironmentVariable / registry");

        foreach (var d in p.Storage)
        {
            Add("storage", d.FriendlyName, Has(d.Media != MediaKind.Unknown), "Storage Management API (MSFT_PhysicalDisk)");
            Add("storage", d.FriendlyName + " — " + Loc.T("hw.storage.health"), Has(d.Health != null || d.WearPercent != null),
                "MSFT_StorageReliabilityCounter", d.WearPercent == null && !p.CapturedElevated ? Loc.T("hw.storage.needsAdmin") : null);
        }

        foreach (var d in p.Displays)
        {
            var name = d.FriendlyName ?? d.DeviceName;
            Add("display", name + " — " + Loc.T("hw.display.mode"), d.CurrentRefreshHz > 0 ? DataConfidence.Measured : DataConfidence.Unavailable, "QueryDisplayConfig");
            Add("display", name + " — " + Loc.T("hw.display.vrr"), d.AdaptiveSyncAdvertised != null ? DataConfidence.Reported : DataConfidence.Unavailable, "EDID",
                d.AdaptiveSyncAdvertised != null ? Loc.T("dq.vrrNote") : null);
            Add("display", name + " — " + Loc.T("hw.display.hdr"), Has(d.HdrSupported != null), "DisplayConfig advanced color info");
        }

        foreach (var n in p.Network.Where(n => n.IsPhysical))
            Add("network", n.Name, n.SpeedMbps > 0 ? DataConfidence.Reported : DataConfidence.Unavailable, "GetAdaptersAddresses");

        Add("windows", Loc.T("hw.os.build"), p.Os.Build > 0 ? DataConfidence.Reported : DataConfidence.Unavailable, "Registry CurrentVersion");
        Add("windows", Loc.T("hw.os.servicing"), p.Os.ServicingEnded != null ? DataConfidence.Reported : DataConfidence.Unavailable,
            "catalog/knowledge/windows-versions.json", p.Os.ServicingNote);
        return list;
    }

    private static bool IsPlaceholder(string s) =>
        s.Contains("To be filled", StringComparison.OrdinalIgnoreCase) || s.Contains("Default string", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("System Product Name", StringComparison.OrdinalIgnoreCase) || s.Equals("Not Applicable", StringComparison.OrdinalIgnoreCase);
}
