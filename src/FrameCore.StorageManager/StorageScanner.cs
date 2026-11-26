using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Platform;

namespace FrameCore.StorageManager;

/// <summary>Physical disks (Storage Management API), health/SMART counters when readable, volumes and TRIM state.</summary>
public sealed class StorageScanner : IHardwareEnricher
{
    private readonly IRegistryAccess _reg;
    public StorageScanner(IRegistryAccess reg) => _reg = reg;
    public string Name => Loc.T("scan.storage");

    public void Enrich(MachineProfile profile) => profile.Storage = Scan();

    public static string BusName(int? bus) => bus switch
    {
        1 => "SCSI", 3 => "ATA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA",
        12 => "SD", 13 => "MMC", 15 => "File-backed", 16 => "Storage Spaces", 17 => "NVMe", _ => bus?.ToString() ?? Loc.Unknown,
    };

    public List<StorageDevice> Scan()
    {
        var devices = new List<StorageDevice>();
        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus, OperationalStatus, FirmwareVersion FROM MSFT_PhysicalDisk", Wmi.Storage);
        var reliability = Wmi.Query("SELECT DeviceId, Temperature, Wear, ReadErrorsUncorrected, PowerOnHours FROM MSFT_StorageReliabilityCounter", Wmi.Storage);
        var partitions = Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage);
        var systemLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";

        foreach (var d in disks)
        {
            var id = d.Str("DeviceId") ?? "";
            var dev = new StorageDevice
            {
                DeviceId = id,
                FriendlyName = d.Str("FriendlyName") ?? Loc.Unknown,
                Media = d.Int("MediaType") switch { 3 => MediaKind.Hdd, 4 => MediaKind.Ssd, 5 => MediaKind.Scm, _ => MediaKind.Unknown },
                Bus = BusName(d.Int("BusType")),
                SizeBytes = d.Long("Size") ?? 0,
                Health = d.Int("HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => null },
                Firmware = d.Str("FirmwareVersion"),
            };
            // NVMe drives are SSDs even when MediaType is "unspecified".
            if (dev.Media == MediaKind.Unknown && dev.Bus == "NVMe") dev.Media = MediaKind.Ssd;
            var rel = reliability.FirstOrDefault(r => r.Str("DeviceId") == id);
            if (rel != null)
            {
                dev.TemperatureC = rel.Int("Temperature") is > 0 and < 150 and var t ? t : null;
                dev.WearPercent = rel.Int("Wear");
                dev.ReadErrorsUncorrected = rel.Long("ReadErrorsUncorrected");
                dev.PowerOnHours = rel.Long("PowerOnHours");
            }
            foreach (var p in partitions.Where(p => p.Str("DiskNumber") == id))
            {
                var letterCode = p.Int("DriveLetter") ?? 0;
                if (letterCode == 0) continue;
                var letter = ((char)letterCode).ToString().ToUpperInvariant();
                try
                {
                    var di = new DriveInfo(letter);
                    if (!di.IsReady) continue;
                    dev.Volumes.Add(new VolumeInfo
                    {
                        Letter = letter, Label = di.VolumeLabel, FileSystem = di.DriveFormat, SizeBytes = di.TotalSize,
                        FreeBytes = di.AvailableFreeSpace, IsSystem = letter == systemLetter,
                    });
                }
                catch { }
            }
            dev.IsSystemDisk = dev.Volumes.Any(v => v.IsSystem);
            devices.Add(dev);
        }

        if (devices.Count == 0) devices.AddRange(FallbackWin32(systemLetter));
        return devices;
    }

    private static IEnumerable<StorageDevice> FallbackWin32(string systemLetter)
    {
        foreach (var d in Wmi.Query("SELECT Model, Size, InterfaceType, Status FROM Win32_DiskDrive"))
            yield return new StorageDevice { FriendlyName = d.Str("Model") ?? "Disk", SizeBytes = d.Long("Size") ?? 0, Bus = d.Str("InterfaceType"), Health = d.Str("Status") };
        // Volumes are still useful even without disk mapping.
        var sys = new StorageDevice { FriendlyName = "Volumes" };
        foreach (var di in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
            sys.Volumes.Add(new VolumeInfo { Letter = di.Name[..1], Label = di.VolumeLabel, FileSystem = di.DriveFormat, SizeBytes = di.TotalSize, FreeBytes = di.AvailableFreeSpace, IsSystem = di.Name.StartsWith(systemLetter, StringComparison.OrdinalIgnoreCase) });
        sys.IsSystemDisk = sys.Volumes.Any(v => v.IsSystem);
        if (sys.Volumes.Count > 0) yield return sys;
    }

    /// <summary>TRIM: HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\DisableDeleteNotification (0 = TRIM enabled).</summary>
    public bool? TrimEnabled()
    {
        var v = _reg.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotification");
        return v == null ? true : RegistryCodec.Encode(v) == "0";
    }
}
