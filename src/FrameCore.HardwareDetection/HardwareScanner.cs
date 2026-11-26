using System.Globalization;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.HardwareDetection.Native;

namespace FrameCore.HardwareDetection;

/// <summary>
/// First-launch / on-demand machine diagnosis. Works without administrator rights; every value that cannot
/// be read stays null ("Indéterminé"). Additional modules contribute through <see cref="IHardwareEnricher"/>.
/// </summary>
public sealed class HardwareScanner
{
    private readonly IRegistryAccess _reg;
    private readonly IReadOnlyList<IHardwareEnricher> _enrichers;

    public HardwareScanner(IRegistryAccess reg, IEnumerable<IHardwareEnricher> enrichers)
    {
        _reg = reg;
        _enrichers = enrichers.ToList();
    }

    public MachineProfile Scan(IProgress<string>? progress = null)
    {
        var p = new MachineProfile { AppVersion = AppInfo.Version, CapturedElevated = AppInfo.IsElevated };
        void Step(string what, Action a)
        {
            progress?.Report(what);
            try { a(); }
            catch (Exception ex) { p.Warnings.Add($"{what}: {ex.Message}"); Log.Error("Scan", what, ex); }
        }
        Step(Loc.T("scan.os"), () => p.Os = DetectOs());
        Step(Loc.T("scan.board"), () => p.Board = DetectBoard());
        Step(Loc.T("scan.cpu"), () => p.Cpu = DetectCpu(p.Board));
        Step(Loc.T("scan.gpu"), () => p.Gpus = DetectGpus());
        Step(Loc.T("scan.memory"), () => p.Memory = DetectMemory());
        Step(Loc.T("scan.firmware"), () => p.Firmware = DetectFirmware(p));
        Step(Loc.T("scan.security"), () => p.Security = DetectSecurity());
        Step(Loc.T("scan.tools"), () => p.InstalledVendorTools = DetectVendorTools());
        foreach (var e in _enrichers) Step(e.Name, () => e.Enrich(p));
        Step(Loc.T("scan.quality"), () => p.DataQuality = DataQualityAssessor.Assess(p));
        p.CapturedUtc = DateTime.UtcNow;
        return p;
    }

    // ---------------------------------------------------------------- OS
    public OsInfo DetectOs()
    {
        const string cv = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string? S(string n) => _reg.GetValue(cv, n)?.Data?.ToString();
        var build = int.TryParse(S("CurrentBuildNumber"), out var b) ? b : Environment.OSVersion.Version.Build;
        var ubr = _reg.GetValue(cv, "UBR") is { } u ? Convert.ToInt32(u.Data, CultureInfo.InvariantCulture) : 0;
        var product = S("ProductName") ?? "Windows";
        // ProductName still says "Windows 10" on Windows 11 installations.
        if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
        var os = new OsInfo
        {
            ProductName = product,
            EditionId = S("EditionID"),
            DisplayVersion = S("DisplayVersion") ?? S("ReleaseId"),
            Build = build,
            Ubr = ubr,
            Architecture = AppInfo.OsArchitecture.ToString(),
            KernelVersion = $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{build}.{ubr}",
            LastBootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64),
            Language = CultureInfo.InstalledUICulture.Name,
            PendingReboot = _reg.KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
                            || _reg.KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"),
        };
        var (version, ended, note) = WindowsVersions.Evaluate(build, os.EditionId, DateTime.Today);
        os.DisplayVersion ??= version;
        os.ServicingEnded = ended;
        os.ServicingNote = note;
        return os;
    }

    // ---------------------------------------------------------------- Board
    public BoardInfo DetectBoard()
    {
        var bb = Wmi.First("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
        var cs = Wmi.First("SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber, PCSystemType FROM Win32_ComputerSystem");
        var enc = Wmi.First("SELECT ChassisTypes FROM Win32_SystemEnclosure");
        var chassis = enc.Array<int>("ChassisTypes") ?? Array.Empty<int>();
        var hasBattery = Wmi.Query("SELECT Name FROM Win32_Battery").Count > 0;
        var power = SystemInterop.PowerCapabilities();
        var board = new BoardInfo
        {
            Manufacturer = Clean(bb.Str("Manufacturer")),
            Product = Clean(bb.Str("Product")),
            Version = Clean(bb.Str("Version")),
            SystemManufacturer = Clean(cs.Str("Manufacturer")),
            SystemModel = Clean(cs.Str("Model")),
            SystemFamily = Clean(cs.Str("SystemFamily")),
            SystemSku = Clean(cs.Str("SystemSKUNumber")),
            ChassisType = chassis.FirstOrDefault(),
            HasBattery = hasBattery || power?.batteries == true,
        };
        board.IsLaptop = Identification.IsLaptopChassis(chassis) || cs.Int("PCSystemType") == 2 || (board.HasBattery && Identification.IsLaptopChassis(chassis.DefaultIfEmpty(0)));
        board.Chipset = Identification.ChipsetFromBoard(board.Product) ?? DetectIntelChipsetFromPci();
        var oem = new[] { "Dell", "HP", "Hewlett", "Lenovo", "Acer", "Microsoft", "Samsung", "Huawei", "Razer", "Alienware", "Fujitsu", "Toshiba", "Dynabook" };
        board.IsOem = oem.Any(o => (board.SystemManufacturer ?? "").Contains(o, StringComparison.OrdinalIgnoreCase));
        return board;
    }

    private static string? DetectIntelChipsetFromPci()
    {
        foreach (var row in Wmi.Query("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%LPC%' OR Name LIKE '%eSPI%'"))
        {
            var m = Regex.Match(row.Str("Name") ?? "", @"\b([BHZQW]\d{3})\b");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t is "To be filled by O.E.M." or "Default string" or "System Product Name" or "System manufacturer" or "Not Applicable" or "None" ? null : t;
    }

    // ---------------------------------------------------------------- CPU
    public CpuInfo DetectCpu(BoardInfo board)
    {
        var w = Wmi.First("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor");
        var brand = SystemInterop.CpuIdAvailable ? SystemInterop.CpuBrandString() : null;
        var name = Regex.Replace((brand ?? w.Str("Name") ?? "").Trim(), @"\s+", " ");
        var vendorStr = SystemInterop.CpuIdAvailable ? SystemInterop.CpuVendorString() : w.Str("Manufacturer");
        var cpu = new CpuInfo
        {
            Name = name,
            Vendor = Identification.CpuVendorFrom(vendorStr, name),
            Cores = w.Int("NumberOfCores") ?? Environment.ProcessorCount,
            Threads = w.Int("NumberOfLogicalProcessors") ?? Environment.ProcessorCount,
            MaxMhz = w.Int("MaxClockSpeed"),
            L2Kb = w.Int("L2CacheSize"),
            L3Kb = w.Int("L3CacheSize"),
            Socket = w.Str("SocketDesignation"),
            Features = SystemInterop.CpuFeatures(),
        };
        // SMBIOS "MaxClockSpeed" is usually the base clock.
        cpu.BaseMhz = cpu.MaxMhz;
        if (SystemInterop.CpuIdAvailable)
        {
            var (family, model, stepping) = SystemInterop.CpuSignature();
            cpu.Family = family; cpu.Model = model; cpu.Stepping = stepping;
            cpu.Microarchitecture = cpu.Vendor == CpuVendor.Intel ? Identification.IntelMicroarchitecture(family, model)
                                  : cpu.Vendor == CpuVendor.Amd ? Identification.AmdMicroarchitecture(family, model) : null;
        }
        var topo = SystemInterop.GetTopology();
        if (topo != null)
        {
            cpu.Cores = topo.Cores;
            cpu.Threads = topo.LogicalProcessors > 0 ? topo.LogicalProcessors : cpu.Threads;
            cpu.IsHybrid = topo.Hybrid;
            cpu.PerformanceCores = topo.PerformanceCores;
            cpu.EfficiencyCores = topo.EfficiencyCores;
            cpu.L3Groups = topo.L3CacheSizes.Count;
            if (topo.L3CacheSizes.Count > 0) cpu.L3Kb = (int)(topo.L3CacheSizes.Sum() / 1024);
            if (topo.L2TotalBytes > 0) cpu.L2Kb = (int)(topo.L2TotalBytes / 1024);
        }
        cpu.Generation = Identification.Generation(cpu.Vendor, cpu.Name);
        cpu.IsX3D = Identification.IsX3D(cpu.Name);
        cpu.IsDualCcdX3D = Identification.IsDualCcdX3D(cpu.Name, cpu.L3Groups, cpu.Cores);
        cpu.IsMobile = Identification.IsMobileCpu(cpu.Name) || board.IsLaptop;
        cpu.Unlocked = Identification.IsUnlocked(cpu.Vendor, cpu.Name, cpu.IsMobile);
        return cpu;
    }

    // ---------------------------------------------------------------- GPU
    public List<GpuInfo> DetectGpus()
    {
        var dxgi = GraphicsInterop.EnumerateDxgi();
        var kmt = GraphicsInterop.EnumerateKmt();
        var wmi = Wmi.Query("SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterCompatibility FROM Win32_VideoController");
        var hwSch = _reg.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        var list = new List<GpuInfo>();

        foreach (var d in dxgi.Where(d => !d.IsSoftware).GroupBy(d => d.Luid).Select(g => g.First()))
        {
            var g = new GpuInfo
            {
                Name = d.Description, VendorId = d.VendorId, DeviceId = d.DeviceId, SubSysId = d.SubSysId,
                DedicatedVramBytes = d.DedicatedVideoMemory, SharedMemoryBytes = d.SharedSystemMemory, Luid = d.Luid,
                Vendor = Identification.GpuVendorFrom(d.VendorId, d.Description),
            };
            var ven = $"VEN_{d.VendorId:X4}&DEV_{d.DeviceId:X4}";
            var row = wmi.FirstOrDefault(r => (r.Str("PNPDeviceID") ?? "").Contains(ven, StringComparison.OrdinalIgnoreCase));
            if (row != null)
            {
                g.PnpDeviceId = row.Str("PNPDeviceID");
                g.DriverVersion = row.Str("DriverVersion");
                g.DriverDate = row.Date("DriverDate");
                g.DriverProvider = row.Str("AdapterCompatibility");
            }
            var k = kmt.FirstOrDefault(x => x.Luid == d.Luid);
            if (k != null)
            {
                g.WddmVersion = k.WddmVersion;
                g.HagsSupported = k.HagsSupported;
                g.HagsEnabled = k.HagsEnabled;
                g.HagsEnabledByDefault = k.HagsEnabledByDefault;
                g.IsHybridDiscrete = k.HybridDiscrete == true;
                g.IsIntegrated = k.HybridIntegrated == true;
            }
            if (g.HagsEnabled == null && hwSch != null) g.HagsEnabled = RegistryCodec.Encode(hwSch) == "2";
            // Integrated GPUs expose little or no dedicated memory (UMA).
            if (!g.IsIntegrated && (g.Vendor == GpuVendor.Intel && !d.Description.Contains("Arc", StringComparison.OrdinalIgnoreCase) && d.DedicatedVideoMemory < 1L << 30))
                g.IsIntegrated = true;
            if (!g.IsIntegrated && g.Vendor == GpuVendor.Amd && d.DedicatedVideoMemory <= 1L << 30 && !Regex.IsMatch(d.Description, @"RX\s*\d", RegexOptions.IgnoreCase))
                g.IsIntegrated = true;
            g.Architecture = Identification.GpuArchitecture(g.Vendor, g.Name);
            DetectResizableBar(g);
            list.Add(g);
        }

        // Fallback when DXGI is unavailable (e.g. remote session): WMI only, VRAM unknown (AdapterRAM is capped at 4 GB).
        if (list.Count == 0)
        {
            foreach (var r in wmi)
            {
                var name = r.Str("Name") ?? "GPU";
                var vid = Regex.Match(r.Str("PNPDeviceID") ?? "", @"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                var g = new GpuInfo
                {
                    Name = name,
                    VendorId = vid.Success ? int.Parse(vid.Groups[1].Value, NumberStyles.HexNumber) : 0,
                    PnpDeviceId = r.Str("PNPDeviceID"), DriverVersion = r.Str("DriverVersion"), DriverDate = r.Date("DriverDate"),
                    DriverProvider = r.Str("AdapterCompatibility"),
                };
                g.Vendor = Identification.GpuVendorFrom(g.VendorId, name);
                g.IsSoftware = g.Vendor == GpuVendor.Microsoft;
                g.Architecture = Identification.GpuArchitecture(g.Vendor, name);
                list.Add(g);
            }
        }
        return list;
    }

    /// <summary>
    /// Resizable BAR heuristic: size of the largest PCI memory window assigned to the GPU. A window close to the VRAM
    /// size means ReBAR is active; a 256 MB window on a large-VRAM card means it is not. Otherwise: unknown.
    /// </summary>
    private static void DetectResizableBar(GpuInfo g)
    {
        if (g.PnpDeviceId == null || g.IsIntegrated) return;
        try
        {
            var ranges = Wmi.Query("SELECT Antecedent, Dependent FROM Win32_PnPAllocatedResource")
                .Where(r => (r.Str("Dependent") ?? "").Replace(@"\\", @"\").Contains(g.PnpDeviceId, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Str("Antecedent") ?? "")
                .Where(a => a.Contains("Win32_DeviceMemoryAddress", StringComparison.OrdinalIgnoreCase))
                .Select(a => Regex.Match(a, "StartingAddress=\"?(\\d+)").Groups[1].Value)
                .Where(s => s.Length > 0).ToHashSet();
            if (ranges.Count == 0) return;
            var mems = Wmi.Query("SELECT StartingAddress, EndingAddress FROM Win32_DeviceMemoryAddress")
                .Where(m => ranges.Contains(m.Str("StartingAddress") ?? "-"))
                .Select(m => (start: (ulong)(m.Long("StartingAddress") ?? 0), end: (ulong)(m.Long("EndingAddress") ?? 0))).ToList();
            if (mems.Count == 0) return;
            var largest = mems.Max(m => m.end - m.start + 1);
            var vram = (ulong)(g.DedicatedVramBytes ?? 0);
            if (vram > 0 && largest >= Math.Min(vram, 2UL << 30))
            {
                g.ResizableBar = ResizableBarState.Enabled;
                g.ResizableBarDetail = Loc.T("gpu.rebar.window", largest / (1024 * 1024));
            }
            else if (vram > (1UL << 30) && largest <= 512UL << 20)
            {
                g.ResizableBar = ResizableBarState.Disabled;
                g.ResizableBarDetail = Loc.T("gpu.rebar.window", largest / (1024 * 1024));
            }
        }
        catch (Exception ex) { Log.Warn("ReBAR", ex.Message); }
    }

    // ---------------------------------------------------------------- Memory
    public MemoryInfo DetectMemory()
    {
        var info = new MemoryInfo();
        var status = SystemInterop.MemoryStatus();
        if (status != null) info.TotalBytes = (long)status.Value.ullTotalPhys;
        foreach (var r in Wmi.Query("SELECT BankLabel, DeviceLocator, Capacity, Manufacturer, PartNumber, SerialNumber, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, FormFactor, ConfiguredVoltage FROM Win32_PhysicalMemory"))
        {
            info.Modules.Add(new MemoryModule
            {
                Slot = r.Str("DeviceLocator"),
                Bank = r.Str("BankLabel"),
                CapacityBytes = r.Long("Capacity") ?? 0,
                Manufacturer = Clean(r.Str("Manufacturer")),
                PartNumber = Clean(r.Str("PartNumber")),
                SerialNumber = r.Str("SerialNumber"),
                RatedSpeedMts = r.Int("Speed") is > 0 and var s ? s : null,
                ConfiguredSpeedMts = r.Int("ConfiguredClockSpeed") is > 0 and var c ? c : null,
                Type = Identification.MemoryType(r.Int("SMBIOSMemoryType")),
                FormFactor = r.Int("FormFactor") switch { 8 => "DIMM", 12 => "SO-DIMM", 0 => null, var f => f?.ToString() },
                ConfiguredVoltageMv = r.Int("ConfiguredVoltage") is > 0 and var v ? v : null,
            });
        }
        info.SlotsTotal = Wmi.First("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray WHERE Use=3").Int("MemoryDevices")
                          ?? Wmi.First("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Int("MemoryDevices");
        return info;
    }

    // ---------------------------------------------------------------- Firmware
    public FirmwareInfo DetectFirmware(MachineProfile p)
    {
        var bios = Wmi.First("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS");
        var fw = new FirmwareInfo
        {
            Vendor = bios.Str("Manufacturer"),
            Version = bios.Str("SMBIOSBIOSVersion"),
            ReleaseDate = bios.Date("ReleaseDate"),
            SmbiosVersion = bios.Int("SMBIOSMajorVersion") is { } maj ? $"{maj}.{bios.Int("SMBIOSMinorVersion") ?? 0}" : null,
            Mode = SystemInterop.FirmwareType() switch { 2 => FirmwareMode.Uefi, 1 => FirmwareMode.Legacy, _ => FirmwareMode.Unknown },
        };
        var sb = _reg.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        fw.SecureBoot = sb == null ? (fw.Mode == FirmwareMode.Legacy ? TriState.No : TriState.Unknown) : RegistryCodec.Encode(sb) == "1" ? TriState.Yes : TriState.No;
        var (present, version) = SystemInterop.Tpm();
        fw.TpmPresent = present;
        fw.TpmVersion = version;
        fw.ModernStandby = SystemInterop.PowerCapabilities()?.aoac ?? false;
        return fw;
    }

    // ---------------------------------------------------------------- Security
    public SecurityInfo DetectSecurity()
    {
        var s = new SecurityInfo();
        var dg = Wmi.First("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning, AvailableSecurityProperties FROM Win32_DeviceGuard", Wmi.DeviceGuard);
        if (dg != null)
        {
            var vbs = dg.Int("VirtualizationBasedSecurityStatus");
            s.VbsRunning = vbs == 2 ? TriState.Yes : vbs is 0 or 1 ? TriState.No : TriState.Unknown;
            var running = dg.Array<int>("SecurityServicesRunning") ?? Array.Empty<int>();
            s.HvciRunning = running.Contains(2) ? TriState.Yes : TriState.No;
            s.CredentialGuardRunning = running.Contains(1) ? TriState.Yes : TriState.No;
            var available = dg.Array<int>("AvailableSecurityProperties");
            s.MbecAvailable = available == null ? TriState.Unknown : available.Contains(7) ? TriState.Yes : TriState.No;
        }
        var features = Wmi.Query("SELECT Name, InstallState FROM Win32_OptionalFeature WHERE Name='VirtualMachinePlatform' OR Name='Microsoft-Hyper-V-All'");
        TriState F(string n) => features.FirstOrDefault(f => f.Str("Name") == n)?.Int("InstallState") switch { 1 => TriState.Yes, 2 or 3 => TriState.No, _ => TriState.Unknown };
        s.VirtualMachinePlatformEnabled = F("VirtualMachinePlatform");
        s.HyperVEnabled = F("Microsoft-Hyper-V-All");
        var mp = Wmi.First("SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender");
        s.DefenderRealTime = mp.Bool("RealTimeProtectionEnabled") switch { true => TriState.Yes, false => TriState.No, _ => TriState.Unknown };
        var fwv = _reg.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall");
        s.FirewallEnabled = fwv == null ? TriState.Unknown : RegistryCodec.Encode(fwv) == "1" ? TriState.Yes : TriState.No;
        return s;
    }

    // ---------------------------------------------------------------- Installed software
    public static readonly (string pattern, string tool)[] VendorToolPatterns =
    {
        ("NVIDIA App", "NVIDIA App"), ("GeForce Experience", "NVIDIA GeForce Experience"), ("AMD Software", "AMD Software: Adrenalin"),
        ("Ryzen Master", "AMD Ryzen Master"), ("AMD Chipset Software", "AMD Chipset Software"), ("Extreme Tuning Utility", "Intel XTU"),
        ("Driver & Support Assistant", "Intel Driver & Support Assistant"), ("Intel Graphics Software", "Intel Graphics Software"),
        ("Arc Control", "Intel Arc Control"), ("MSI Afterburner", "MSI Afterburner"), ("PresentMon", "Intel PresentMon"),
        ("PawnIO", "PawnIO"), ("Armoury Crate", "ASUS Armoury Crate"), ("MSI Center", "MSI Center"), ("GIGABYTE Control Center", "GIGABYTE Control Center"),
        ("Lenovo Vantage", "Lenovo Vantage"), ("Legion", "Lenovo Legion Space"), ("OMEN Gaming Hub", "HP OMEN Gaming Hub"), ("Alienware Command Center", "Alienware Command Center"),
        ("Dell Command | Update", "Dell Command Update"), ("HP Support Assistant", "HP Support Assistant"), ("Process Lasso", "Process Lasso"),
        ("HWiNFO", "HWiNFO"), ("Logi", "Logitech G HUB"), ("Razer Synapse", "Razer Synapse"), ("iCUE", "Corsair iCUE"), ("SteelSeries GG", "SteelSeries GG"),
    };

    public List<InstalledProgram> InstalledPrograms()
    {
        var list = new List<InstalledProgram>();
        foreach (var root in new[]
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        })
        {
            foreach (var sub in _reg.GetSubKeyNames(root))
            {
                var path = root + "\\" + sub;
                var name = _reg.GetValue(path, "DisplayName")?.Data?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (_reg.GetValue(path, "SystemComponent") is { } sc && RegistryCodec.Encode(sc) == "1") continue;
                var date = _reg.GetValue(path, "InstallDate")?.Data?.ToString();
                list.Add(new InstalledProgram
                {
                    Name = name.Trim(),
                    Publisher = _reg.GetValue(path, "Publisher")?.Data?.ToString(),
                    Version = _reg.GetValue(path, "DisplayVersion")?.Data?.ToString(),
                    InstallLocation = _reg.GetValue(path, "InstallLocation")?.Data?.ToString(),
                    UninstallString = _reg.GetValue(path, "UninstallString")?.Data?.ToString(),
                    InstallDate = DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                });
            }
        }
        return list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(p => p.Name).ToList();
    }

    public List<string> DetectVendorTools()
    {
        var programs = InstalledPrograms();
        var found = new List<string>();
        foreach (var (pattern, tool) in VendorToolPatterns)
            if (programs.Any(p => p.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))) found.Add(tool);
        if (!found.Contains("PawnIO") && File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "PawnIO.sys"))) found.Add("PawnIO");
        return found.Distinct().ToList();
    }
}
