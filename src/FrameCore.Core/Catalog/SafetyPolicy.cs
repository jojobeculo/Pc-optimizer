using FrameCore.Core.Models;

namespace FrameCore.Core.Catalog;

/// <summary>
/// Non-negotiable safety rules. Enforced when the catalog is loaded, again by the elevated worker
/// before executing anything, and by the startup/services managers for dynamic changes.
/// </summary>
public static class SafetyPolicy
{
    /// <summary>Services FrameCore never modifies (security, updates, core OS, audio, networking, anti-cheat).</summary>
    public static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        // Security
        "WinDefend", "WdNisSvc", "WdNisDrv", "WdFilter", "WdBoot", "Sense", "SecurityHealthService", "wscsvc", "mpssvc", "mpsdrv", "BFE",
        "SgrmBroker", "KeyIso", "VaultSvc", "SamSs", "Netlogon", "EFS", "BDESVC", "TPM", "CryptSvc", "WebThreatDefSvc", "WebThreatDefUserSvc",
        "AppIDSvc", "ProfSvc", "gpsvc", "LSM", "lsass",
        // Windows Update & servicing
        "wuauserv", "UsoSvc", "WaaSMedicSvc", "BITS", "TrustedInstaller", "DoSvc", "InstallService", "msiserver",
        // Core OS
        "RpcSs", "RpcEptMapper", "DcomLaunch", "EventLog", "Winmgmt", "PlugPlay", "Power", "BrokerInfrastructure", "SystemEventsBroker",
        "CoreMessagingRegistrar", "Schedule", "StateRepository", "AppXSvc", "ClipSVC", "TokenBroker", "UserManager", "Themes",
        "DeviceInstall", "DsmSvc", "camsvc", "TimeBrokerSvc", "TextInputManagementService", "WpnService", "Appinfo", "seclogon",
        "EventSystem", "SENS", "ShellHWDetection", "FontCache", "Wcmsvc", "WinHttpAutoProxySvc", "LanmanWorkstation",
        // Networking
        "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm", "WlanSvc", "Netman", "iphlpsvc",
        // Audio / input / display
        "Audiosrv", "AudioEndpointBuilder", "hidserv", "TabletInputService", "DispBrokerDesktopSvc", "GraphicsPerfSvc",
        // Bluetooth (peripherals)
        "bthserv", "BTAGService", "BluetoothUserService",
        // Xbox / Game Pass / controllers (already demand-start)
        "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc", "GamingServices", "GamingServicesNet",
        // Anti-cheat
        "vgc", "vgk", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "FACEIT", "FACEITService", "EAAntiCheatService",
        "PnkBstrA", "PnkBstrB", "ESEADriver2", "nProtect", "xhunter1", "Vanguard", "mhyprot2", "atvi-randgrid_sr",
        // GPU drivers
        "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem", "AMD External Events Utility", "amdfendr", "amdfendrmgr", "AMD Crash Defender Service",
        "igfxCUIService2.0.0.0", "igccservice", "cplspcon",
    };

    private static readonly string[] ProtectedRegistryPrefixes =
    {
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender",
        @"HKLM\SOFTWARE\Microsoft\Windows Defender",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
        @"HKLM\SOFTWARE\Microsoft\WindowsUpdate",
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy",
        @"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall",
        @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard",
        @"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa",
        @"HKLM\SOFTWARE\Policies\Microsoft\FVE",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
        @"HKLM\SYSTEM\CurrentControlSet\Services\WinDefend",
    };

    private static readonly string[] ProtectedRegistryValues =
    {
        "FeatureSettingsOverride", "FeatureSettingsOverrideMask", // Spectre/Meltdown mitigations
        "EnableVirtualizationBasedSecurity", "HypervisorEnforcedCodeIntegrity",
        "DisableAntiSpyware", "DisableRealtimeMonitoring", "EnableLUA", "ConsentPromptBehaviorAdmin",
    };

    private static readonly string[] ProtectedTaskPrefixes =
    {
        @"\Microsoft\Windows\WindowsUpdate", @"\Microsoft\Windows\UpdateOrchestrator", @"\Microsoft\Windows\Windows Defender",
        @"\Microsoft\Windows\WaaSMedic", @"\Microsoft\Windows\InstallService", @"\Microsoft\Windows\TPM", @"\Microsoft\Windows\BitLocker",
        @"\Microsoft\Windows\ExploitGuard", @"\Microsoft\Windows\AppID",
    };

    /// <summary>Only these BCD elements may be removed (they are "forced" overrides; removal restores Windows' own choice).</summary>
    public static readonly HashSet<string> RemovableBcdElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "useplatformclock", "disabledynamictick", "useplatformtick", "tscsyncpolicy",
    };

    public static readonly HashSet<string> ToggleableOptionalFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "VirtualMachinePlatform",
    };

    public static readonly HashSet<string> AllowedAdapterKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "*EEE", "*InterruptModeration", "EEELinkAdvertisement", "AdvancedEEE", "EnableGreenEthernet", "GreenEthernet", "PowerSavingMode",
    };

    public static bool IsProtectedService(string name) => ProtectedServices.Contains(name);

    /// <summary>
    /// The only security settings FrameCore may change, and only through Extreme-only, consent-gated, reversible catalog
    /// entries (enforced by catalog validation): Memory integrity (HVCI) as documented by Microsoft for gamers, and two
    /// scoped Microsoft Defender preferences (scheduled-scan CPU limit, game-folder exclusions). Real-time protection,
    /// the firewall, Windows Update, Secure Boot, TPM, BitLocker and CPU mitigations are never touched.
    /// </summary>
    private static readonly (string Path, string Name)[] SecurityTradeOffRegistry =
    {
        (@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"),
    };
    public static readonly string[] SecurityTradeOffHandlers = { "defender.scanCpuLimit", "defender.gameFolderExclusions" };

    public static bool IsSecurityTradeOff(ActionDefinition a) =>
        (a.Type == "registry" && a.Path != null && SecurityTradeOffRegistry.Any(x => string.Equals(NormalizeRegistryPath(a.Path), x.Path, StringComparison.OrdinalIgnoreCase)
                                                                                    && string.Equals(a.Name, x.Name, StringComparison.OrdinalIgnoreCase)))
        || (a.Type == "handler" && SecurityTradeOffHandlers.Contains(a.Name, StringComparer.OrdinalIgnoreCase));

    public static bool IsProtectedRegistry(string path, string? valueName)
    {
        var p = NormalizeRegistryPath(path);
        if (SecurityTradeOffRegistry.Any(x => string.Equals(p, x.Path, StringComparison.OrdinalIgnoreCase) && string.Equals(valueName, x.Name, StringComparison.OrdinalIgnoreCase))) return false;
        if (ProtectedRegistryPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return true;
        return valueName != null && ProtectedRegistryValues.Contains(valueName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsProtectedTask(string path) =>
        ProtectedTaskPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeRegistryPath(string path)
    {
        var p = path.Replace('/', '\\').Trim();
        if (p.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)) p = "HKLM" + p[18..];
        else if (p.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)) p = "HKCU" + p[17..];
        p = p.Replace(@"\CurrentControlSet\", @"\CurrentControlSet\", StringComparison.OrdinalIgnoreCase);
        p = System.Text.RegularExpressions.Regex.Replace(p, @"\\ControlSet00\d\\", @"\CurrentControlSet\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return p;
    }

    /// <summary>Returns violations for a single action (empty = allowed).</summary>
    public static IEnumerable<string> Check(ActionDefinition a)
    {
        switch (a.Type)
        {
            case "registry" when a.Path != null && IsProtectedRegistry(a.Path, a.Name):
                yield return $"registry target is protected: {a.Path}\\{a.Name}";
                break;
            case "service" when a.Name != null && IsProtectedService(a.Name):
                yield return $"service is protected: {a.Name}";
                break;
            case "task" when a.Path != null && IsProtectedTask(a.Path) && a.Enabled == false:
                yield return $"task is protected: {a.Path}";
                break;
            case "task" when a.Path != null && a.Path.EndsWith(@"\Defrag\ScheduledDefrag", StringComparison.OrdinalIgnoreCase) && a.Enabled == false:
                yield return "storage optimization task may only be enabled";
                break;
            case "bcdRemove" when a.Name != null && !RemovableBcdElements.Contains(a.Name):
                yield return $"BCD element not allowed: {a.Name}";
                break;
            case "optionalFeature" when a.Name != null && !ToggleableOptionalFeatures.Contains(a.Name):
                yield return $"optional feature not allowed: {a.Name}";
                break;
            case "netAdapterProperty" when a.Name != null && !AllowedAdapterKeywords.Contains(a.Name):
                yield return $"adapter keyword not allowed: {a.Name}";
                break;
        }
    }
}
