# Étude des technologies et des API

## 1. Choix de la plateforme

| Option | Avantages | Inconvénients | Décision |
|---|---|---|---|
| **C# / .NET 10 LTS + WPF** | LTS (support jusqu'à novembre 2028), P/Invoke et COM natifs, WMI (`System.Management`), compilation **autonome** (aucun runtime à installer), écosystème mûr, compilable depuis Linux (`EnableWindowsTargeting`) | WPF n'est pas « Fluent » par défaut → thème à concevoir | **Retenu** |
| WinUI 3 / Windows App SDK | Look Windows 11 natif | Empaquetage plus complexe, compilation XAML uniquement sous Windows, dépendance au runtime Windows App SDK | Écarté (non compilable dans l'environnement de build, déploiement plus fragile) |
| Electron / Tauri | UI web | Accès système indirect, taille, sécurité (surface JS) | Écarté |
| C++ / Win32 | Contrôle total | Coût de développement, risque mémoire | Écarté (réservé si un pont natif devient indispensable) |

Architecture MVVM avec **CommunityToolkit.Mvvm** (MIT) et **Microsoft.Extensions.DependencyInjection** (MIT).

## 2. Inventaire des API utilisées

| Besoin | API retenue | Droits | Limites connues |
|---|---|---|---|
| Identité CPU, génération, hybride | `CPUID` via `System.Runtime.Intrinsics.X86.X86Base.CpuId` (aucun pilote) + WMI `Win32_Processor` + `GetLogicalProcessorInformationEx` (classe d'efficacité des cœurs, topologie L3 → nombre de CCD) | Utilisateur | Fréquence max annoncée = valeur SMBIOS (souvent la base) |
| Utilisation/fréquence CPU, throttling | PDH avec `PdhAddEnglishCounterW` (indépendant de la langue) : `Processor Information(*)\% Processor Utility`, `% Processor Performance`, `Processor Frequency`, `% Performance Limit` | Utilisateur | Fréquence effective = fréquence nominale × % performance (même méthode que le Gestionnaire des tâches) |
| Températures/tensions/puissance CPU | LibreHardwareMonitor 0.9.6 + PawnIO (si installé) dans le processus élevé | Admin + PawnIO | Sans PawnIO : « Indéterminé » ; repli ACPI `MSAcpi_ThermalZoneTemperature` signalé comme approximatif |
| GPU (tous fabricants) | DXGI (`IDXGIFactory1::EnumAdapters1` → VRAM dédiée exacte, LUID), PDH `GPU Engine(*)\Utilization Percentage`, `GPU Adapter Memory(*)\Dedicated Usage`, D3DKMT (`KMTQAITYPE_WDDM_2_7_CAPS` = 70 → support/état HAGS ; `KMTQAITYPE_DRIVERVERSION` = 13 → version WDDM ; `KMTQAITYPE_ADAPTERTYPE` = 15 → iGPU/dGPU hybride) | Utilisateur | `Win32_VideoController.AdapterRAM` est plafonné à 4 Go (uint32) → non utilisé pour la VRAM |
| NVIDIA | **NVML** (`nvml.dll` fourni avec le pilote) : températures, fréquences, puissance, ventilateur, raisons de limitation (`nvmlDeviceGetCurrentClocksEventReasons`), PCIe, VBIOS, BAR1 (détection Resizable BAR), plage de limite de puissance, **offsets d'horloge** (`nvmlDeviceGetClockOffsets/SetClockOffsets`, struct `nvmlClockOffset_v1_t`, version `0x01000018`) | Lecture : utilisateur. Écriture : administrateur | GeForce : support selon génération/pilote (Maxwell+ annoncé, retours contradictoires) → toute erreur `NOT_SUPPORTED` est affichée telle quelle. Les offsets ne sont pas persistés au redémarrage (sécurité) |
| AMD Radeon | **ADLX** (`amdadlx64.dll` fourni avec Adrenalin) via les **vtables C** documentées : `IADLXSystem → GetGPUs / GetGPUTuningServices`, `IADLXGPUTuningServices.IsSupportedAutoTuning / GetAutoTuning / GetManualPowerTuning`, `IADLXGPUAutoTuning.StartUndervoltGPU / StartOverclockGPU / StartOverclockVRAM`, `ResetToFactory` | Utilisateur (selon pilote) | Les liaisons C# officielles exigent une compilation SWIG/MSVC ; FrameCore appelle directement l'ABI C (ordre des méthodes vérifié dans les en-têtes officiels) |
| Intel Arc | PDH (utilisation, VRAM) ; tuning : renvoi vers Intel Graphics Software | — | L'Intel Graphics Control Library (IGCL) expose du tuning mais n'est pas encore intégrée (feuille de route) |
| RAM | WMI `Win32_PhysicalMemory` (`Speed`, `ConfiguredClockSpeed`, `SMBIOSMemoryType`, `PartNumber`, `DeviceLocator`), `GlobalMemoryStatusEx`, `GetPerformanceInfo`, PDH `Memory\*` (listes Standby/Modified/Free) | Utilisateur | Le SMBIOS ne donne pas toujours le profil XMP/EXPO ; FrameCore décode la référence fabricant quand c'est possible et sinon affiche « Indéterminé ». Timings : uniquement via LHM/PawnIO (SPD) |
| Carte mère / BIOS | WMI `Win32_BaseBoard`, `Win32_BIOS`, `Win32_ComputerSystem`, `Win32_SystemEnclosure` ; `GetFirmwareType` (UEFI) ; registre `SecureBoot\State` ; `Tbsi_GetDeviceInfo` (TPM, sans admin) ; `Win32_DeviceMemoryAddress` + `Win32_PnPAllocatedResource` (taille des fenêtres mémoire PCI → Resizable BAR / Above 4G) | Utilisateur | Aucune API publique pour lire les options BIOS ; aucune API publique des fabricants pour la dernière version BIOS → lien vers la page support officielle |
| Stockage | WMI `MSFT_PhysicalDisk` / `MSFT_StorageReliabilityCounter` (root\Microsoft\Windows\Storage), registre `DisableDeleteNotification`, `defrag.exe /L` | Lecture fiabilité : admin | SMART détaillé selon contrôleur |
| Réseau | `System.Net.NetworkInformation`, WMI `MSFT_NetAdapterAdvancedPropertySettingData` (mots-clés normalisés `*EEE`, `*InterruptModeration`, `*RSS`…), `MSFT_NetTCPSetting` | Utilisateur / admin pour modifier | Les noms affichés des propriétés sont localisés → seuls les `RegistryKeyword` sont utilisés |
| Écrans | `EnumDisplaySettingsEx`, `ChangeDisplaySettingsEx` (avec `CDS_TEST`), `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo` (nom du moniteur, type de connexion HDMI/DP, couleur avancée/HDR), EDID (registre `Enum\DISPLAY\…\Device Parameters\EDID`) : plage VRR (descripteur 0xFD), bloc AMD FreeSync, bloc HDMI Forum VRR, métadonnées HDR | Utilisateur | G-SYNC « module » non détectable de façon fiable via EDID → « à vérifier dans le panneau NVIDIA » |
| Pilotes | WMI `Win32_PnPSignedDriver` (version, date, fournisseur, signataire, INF), **Windows Update Agent API** (`Microsoft.Update.Session`, recherche `Type='Driver'`) | Recherche : utilisateur ; installation : admin | Aucun constructeur GPU n'offre d'API publique documentée de « dernière version » → lien officiel + outil officiel détecté |
| Démarrage | Registre Run/RunOnce (HKCU/HKLM/WOW6432Node), `StartupApproved` (même mécanisme que le Gestionnaire des tâches), dossiers Startup, Planificateur de tâches (COM `Schedule.Service`), services (`ServiceController` + registre), extensions shell (`Shell Extensions\Blocked`), icônes de zone de notification (`Control Panel\NotifyIconSettings`) | Variable | — |
| Alimentation | `powrprof.dll` : `PowerGetActiveScheme`, `PowerSetActiveScheme`, `PowerRead/WriteACValueIndex`, `PowerDuplicateScheme`, `PowerEnumerate` (sortie non localisée, contrairement à `powercfg /q`) | Écriture : admin | Mode d'alimentation Windows 11 (« overlay ») : `PowerSetActiveOverlayScheme` est **non documentée** → niveau « Probable », restauration de la valeur lue |
| Sécurité | WMI `Win32_DeviceGuard` (root\Microsoft\Windows\DeviceGuard) : VBS, HVCI, disponibilité MBEC/GMET (propriété 7) | Utilisateur | Lecture seule — aucune modification automatique |
| Capture des frametimes | **ETW** via `Microsoft.Diagnostics.Tracing.TraceEvent` (MIT) : événements `Present_Start/Stop` des fournisseurs Microsoft-Windows-DXGI et Microsoft-Windows-D3D9 → *MsBetweenPresents* ; import CSV PresentMon ; pilotage de la CLI PresentMon 2.x si installée (`--process_name --output_file --timed --terminate_after_timed --stop_existing_session --v1_metrics`) | Admin (session ETW) | Vulkan/OpenGL : utiliser PresentMon (suivi DxgKrnl) |
| Tests de stabilité | CPU : charge déterministe multi-thread avec **vérification croisée des résultats** (une divergence = erreur de calcul). GPU : calcul Direct3D 11 (Vortice, MIT) avec double exécution et comparaison des sorties | Utilisateur | Un test court n'est jamais une garantie de stabilité |
| Points de restauration | WMI `SystemRestore.CreateRestorePoint` (root\default) | Admin | Windows limite à un point par 24 h par défaut (`SystemRestorePointCreationFrequency`) |
| BCD | `bcdedit.exe /enum {current}` et `/deletevalue` (arguments fixes, éléments en liste blanche) | Admin | FrameCore ne fait que **retirer** des forçages (useplatformclock, disabledynamictick…), jamais en ajouter |

## 3. Élévation et liste blanche

- L'application principale s'exécute **sans droits administrateur** (`asInvoker`). Tous les diagnostics
  fonctionnent dans ce mode (avec moins de capteurs).
- Les opérations privilégiées sont envoyées à un **processus élevé** (même exécutable, argument
  `--worker`) démarré via UAC à la demande, connecté par un **canal nommé** dont :
  - le nom est aléatoire et transmis en argument ;
  - l'ACL n'autorise que l'utilisateur courant ;
  - l'identité du client est vérifiée (`GetNamedPipeClientProcessId` = PID lancé) ;
  - le worker vérifie que le serveur est son processus parent.
- Le worker n'accepte que des **requêtes typées** : identifiant d'optimisation du catalogue (chargé
  depuis son propre dossier d'installation), ou opération paramétrée validée (nom de service existant
  et non protégé, entrée de démarrage existante…). Aucune ligne de commande n'est construite à partir
  de texte saisi.
- Les modifications HKCU sont faites par le processus utilisateur (pour ne pas écrire dans le profil
  d'un autre compte en cas d'élévation par un administrateur tiers).

## 4. Sécurité des téléchargements

- Mises à jour de FrameCore : **désactivées par défaut** ; si l'utilisateur configure une URL de
  manifeste, le fichier téléchargé n'est exécuté que si son **SHA-256** correspond au manifeste et que
  sa signature Authenticode est valide (`WinVerifyTrust`).
- Pilotes : uniquement via Windows Update (signature et intégrité gérées par Windows) ou liens
  vers les sites officiels. Aucun pack de pilotes tiers.
- BIOS : jamais téléchargé ni flashé par FrameCore. Lien officiel et procédure du fabricant.

## 5. Dépendances retenues

| Paquet | Version | Licence | Usage |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.x | MIT | MVVM |
| Microsoft.Extensions.DependencyInjection | 10.0.x | MIT | Injection de dépendances |
| System.Management | 10.0.x | MIT | WMI |
| System.ServiceProcess.ServiceController | 10.0.x | MIT | Services |
| Microsoft.Diagnostics.Tracing.TraceEvent | 3.x | MIT | Capture ETW |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | Capteurs avancés (avec PawnIO) |
| Vortice.Direct3D11 / Vortice.D3DCompiler | 3.8.x | MIT | Test de stabilité GPU |
| xunit / Microsoft.NET.Test.Sdk | — | Apache-2.0 / MIT | Tests |

Voir `docs/DEPENDANCES-ET-LICENCES.md` pour l'inventaire complet.
