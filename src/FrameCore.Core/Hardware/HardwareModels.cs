using System.Text.Json.Serialization;
using FrameCore.Core.Models;

namespace FrameCore.Core.Hardware;

// All "unknown" values are represented by null and displayed as "Indéterminé" — never invented.

public sealed class OsInfo
{
    public string ProductName { get; set; } = "";
    public string? EditionId { get; set; }
    public string? DisplayVersion { get; set; }
    public int Build { get; set; }
    public int Ubr { get; set; }
    public string Architecture { get; set; } = "";
    public string? KernelVersion { get; set; }
    public bool IsWindows11 => Build >= 22000;
    public string? ServicingNote { get; set; }
    public bool? ServicingEnded { get; set; }
    public DateTime? LastBootUtc { get; set; }
    public bool PendingReboot { get; set; }
    public string? Language { get; set; }
    public string FullBuild => Ubr > 0 ? $"{Build}.{Ubr}" : Build.ToString();
}

public enum CpuVendor { Unknown, Intel, Amd, Other }

public sealed class CpuInfo
{
    public CpuVendor Vendor { get; set; }
    public string Name { get; set; } = "";
    public int Family { get; set; }
    public int Model { get; set; }
    public int Stepping { get; set; }
    public string? Microarchitecture { get; set; }
    public string? Generation { get; set; }
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int? BaseMhz { get; set; }
    public int? MaxMhz { get; set; }
    public int? L2Kb { get; set; }
    public int? L3Kb { get; set; }
    /// <summary>Number of distinct L3 caches (≈ number of CCDs on AMD desktop parts).</summary>
    public int L3Groups { get; set; }
    public bool IsHybrid { get; set; }
    public int PerformanceCores { get; set; }
    public int EfficiencyCores { get; set; }
    public bool IsX3D { get; set; }
    public bool IsDualCcdX3D { get; set; }
    public TriState Unlocked { get; set; }
    public string? Socket { get; set; }
    public List<string> Features { get; set; } = new();
    public bool IsMobile { get; set; }
}

public enum GpuVendor { Unknown, Nvidia, Amd, Intel, Microsoft, Other }

public enum ResizableBarState { Unknown, Enabled, Disabled }

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public GpuVendor Vendor { get; set; }
    public int VendorId { get; set; }
    public int DeviceId { get; set; }
    public int SubSysId { get; set; }
    public string? PnpDeviceId { get; set; }
    public long? DedicatedVramBytes { get; set; }
    public long? SharedMemoryBytes { get; set; }
    public long Luid { get; set; }
    public bool IsIntegrated { get; set; }
    public bool IsSoftware { get; set; }
    public string? DriverVersion { get; set; }
    public DateTime? DriverDate { get; set; }
    public string? DriverProvider { get; set; }
    /// <summary>WDDM version × 1000 (e.g. 3100 = WDDM 3.1).</summary>
    public int? WddmVersion { get; set; }
    public bool? HagsSupported { get; set; }
    public bool? HagsEnabled { get; set; }
    public bool? HagsEnabledByDefault { get; set; }
    public string? VbiosVersion { get; set; }
    public string? Architecture { get; set; }
    public ResizableBarState ResizableBar { get; set; }
    public string? ResizableBarDetail { get; set; }
    public int? PcieCurrentGen { get; set; }
    public int? PcieCurrentWidth { get; set; }
    public int? PcieMaxGen { get; set; }
    public int? PcieMaxWidth { get; set; }
    public bool IsHybridDiscrete { get; set; }
}

public sealed class MemoryModule
{
    public string? Slot { get; set; }
    public string? Bank { get; set; }
    public long CapacityBytes { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    [JsonIgnore] public string? SerialNumber { get; set; }
    /// <summary>SMBIOS "Speed": maximum capable speed reported by firmware, in MT/s (often the JEDEC speed).</summary>
    public int? RatedSpeedMts { get; set; }
    /// <summary>SMBIOS "Configured Memory Speed": current data rate in MT/s.</summary>
    public int? ConfiguredSpeedMts { get; set; }
    /// <summary>Speed decoded from the vendor part number (XMP/EXPO kit rating) when recognizable.</summary>
    public int? PartNumberSpeedMts { get; set; }
    public string? Type { get; set; }
    public string? FormFactor { get; set; }
    public int? ConfiguredVoltageMv { get; set; }
}

public enum ChannelMode { Unknown, Single, Dual, Quad }

public enum XmpState { Unknown, LikelyEnabled, LikelyDisabled, NotApplicable }

public sealed class MemoryInfo
{
    public long TotalBytes { get; set; }
    public List<MemoryModule> Modules { get; set; } = new();
    public int? SlotsTotal { get; set; }
    public ChannelMode Channels { get; set; }
    public string? ChannelReasoning { get; set; }
    public XmpState Xmp { get; set; }
    public string? XmpReasoning { get; set; }
    public string? Type => Modules.Select(m => m.Type).FirstOrDefault(t => t != null);
    public int? ConfiguredSpeedMts => Modules.Select(m => m.ConfiguredSpeedMts).FirstOrDefault(s => s is > 0);
    /// <summary>Memory clock in MHz = data rate / 2 (DDR). Kept separate from MT/s to avoid the usual confusion.</summary>
    public int? ClockMhz => ConfiguredSpeedMts is { } s ? s / 2 : null;
}

public sealed class BoardInfo
{
    public string? Manufacturer { get; set; }
    public string? Product { get; set; }
    public string? Version { get; set; }
    public string? SystemManufacturer { get; set; }
    public string? SystemModel { get; set; }
    public string? SystemFamily { get; set; }
    public string? SystemSku { get; set; }
    public int? ChassisType { get; set; }
    public bool IsLaptop { get; set; }
    public bool HasBattery { get; set; }
    public string? Chipset { get; set; }
    public bool IsOem { get; set; }
}

public enum FirmwareMode { Unknown, Uefi, Legacy }

public sealed class FirmwareInfo
{
    public string? Vendor { get; set; }
    public string? Version { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? SmbiosVersion { get; set; }
    public FirmwareMode Mode { get; set; }
    public TriState SecureBoot { get; set; }
    public bool? TpmPresent { get; set; }
    public string? TpmVersion { get; set; }
    public bool? Above4GDecoding { get; set; }
    public string? Above4GDetail { get; set; }
    public bool ModernStandby { get; set; }
}

public enum MediaKind { Unknown, Hdd, Ssd, Scm }

public sealed class VolumeInfo
{
    public string Letter { get; set; } = "";
    public string? Label { get; set; }
    public string? FileSystem { get; set; }
    public long SizeBytes { get; set; }
    public long FreeBytes { get; set; }
    public bool IsSystem { get; set; }
    public double FreePercent => SizeBytes > 0 ? 100.0 * FreeBytes / SizeBytes : 0;
}

public sealed class StorageDevice
{
    public string FriendlyName { get; set; } = "";
    public MediaKind Media { get; set; }
    public string? Bus { get; set; }
    public long SizeBytes { get; set; }
    public string? Health { get; set; }
    public string? OperationalStatus { get; set; }
    public string? Firmware { get; set; }
    public int? TemperatureC { get; set; }
    public int? WearPercent { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? PowerOnHours { get; set; }
    public bool IsSystemDisk { get; set; }
    public string? DeviceId { get; set; }
    public List<VolumeInfo> Volumes { get; set; } = new();
}

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = "";
    public string? FriendlyName { get; set; }
    public string? AdapterName { get; set; }
    public string? Connection { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsInternal { get; set; }
    public int CurrentWidth { get; set; }
    public int CurrentHeight { get; set; }
    public double CurrentRefreshHz { get; set; }
    public int? NativeWidth { get; set; }
    public int? NativeHeight { get; set; }
    public double? MaxRefreshAtCurrentResolution { get; set; }
    public double? MaxRefreshAtNativeResolution { get; set; }
    public int? EdidMaxRefreshHz { get; set; }
    public int? VrrMinHz { get; set; }
    public int? VrrMaxHz { get; set; }
    public bool? AdaptiveSyncAdvertised { get; set; }
    public bool? FreeSyncAdvertised { get; set; }
    public bool? HdmiVrrAdvertised { get; set; }
    public bool? HdrSupported { get; set; }
    public bool? HdrEnabled { get; set; }
    public int? BitsPerColor { get; set; }
    public string? ManufacturerCode { get; set; }
    public string? LinkLimitNote { get; set; }
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public long SpeedMbps { get; set; }
    public bool IsUp { get; set; }
    public bool IsPhysical { get; set; }
    public bool IsWireless { get; set; }
    public string? Gateway { get; set; }
    public List<string> DnsServers { get; set; } = new();
    public string? InterfaceGuid { get; set; }
    public Dictionary<string, string> AdvancedProperties { get; set; } = new();
    public string? DriverVersion { get; set; }
}

public sealed class SecurityInfo
{
    public TriState VbsRunning { get; set; }
    public TriState HvciRunning { get; set; }
    public TriState CredentialGuardRunning { get; set; }
    public TriState MbecAvailable { get; set; }
    public TriState VirtualMachinePlatformEnabled { get; set; }
    public TriState HyperVEnabled { get; set; }
    public TriState DefenderRealTime { get; set; }
    public TriState FirewallEnabled { get; set; }
}

public sealed class MachineProfile
{
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    public string AppVersion { get; set; } = "";
    public bool CapturedElevated { get; set; }
    public OsInfo Os { get; set; } = new();
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public BoardInfo Board { get; set; } = new();
    public FirmwareInfo Firmware { get; set; } = new();
    public List<StorageDevice> Storage { get; set; } = new();
    public List<DisplayInfo> Displays { get; set; } = new();
    public List<NetworkAdapterInfo> Network { get; set; } = new();
    public SecurityInfo Security { get; set; } = new();
    public List<string> InstalledVendorTools { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    [JsonIgnore] public GpuInfo? PrimaryGpu =>
        Gpus.Where(g => !g.IsSoftware).OrderByDescending(g => g.IsIntegrated ? 0 : 1).ThenByDescending(g => g.DedicatedVramBytes ?? 0).FirstOrDefault();

    /// <summary>Hardware profile signature, e.g. "AMD + NVIDIA (desktop)".</summary>
    [JsonIgnore] public string Signature
    {
        get
        {
            var cpu = Cpu.Vendor switch { CpuVendor.Amd => "AMD", CpuVendor.Intel => "Intel", _ => "CPU ?" };
            var gpus = Gpus.Where(g => !g.IsSoftware).Select(g => g.Vendor switch
            {
                GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => g.IsIntegrated ? "Radeon (iGPU)" : "Radeon",
                GpuVendor.Intel => g.IsIntegrated ? "Intel (iGPU)" : "Intel Arc", _ => g.Name,
            }).Distinct();
            return $"{cpu} + {string.Join(" + ", gpus)} ({(Board.IsLaptop ? "portable" : "fixe")})";
        }
    }
}

/// <summary>A module that adds information to the machine profile (storage, displays, network, vendor APIs…).</summary>
public interface IHardwareEnricher
{
    string Name { get; }
    void Enrich(MachineProfile profile);
}

public sealed class InstalledProgram
{
    public string Name { get; set; } = "";
    public string? Publisher { get; set; }
    public string? Version { get; set; }
    public string? InstallLocation { get; set; }
    public string? UninstallString { get; set; }
    public DateTime? InstallDate { get; set; }
}
