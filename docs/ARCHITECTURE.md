# Architecture de FrameCore

FrameCore est une application de bureau Windows 11 (x64) écrite en C# / .NET 10 LTS avec une interface WPF
(motif MVVM, CommunityToolkit.Mvvm). Elle est distribuée en mode *self-contained* : aucun runtime .NET n'est à
installer sur la machine cible.

## 1. Principes directeurs

| Principe | Mise en œuvre |
|---|---|
| Rien n'est modifié sans être mesuré puis enregistré | `TweakEngine` lit l'état réel, le persiste dans le journal **avant** toute écriture, applique, relit et vérifie. |
| Tout est réversible ou explicitement signalé comme non réversible | Chaque action a un `Capture` / `Restore` ; les opérations de maintenance (nettoyage, retrim) sont marquées `reversible: false`. |
| Catalogue déclaratif et fermé | Les optimisations sont des fichiers JSON (`catalog/tweaks/*.json`) composés d'un **ensemble fermé de types d'actions** ; aucun script, aucune commande libre. |
| Moindre privilège | L'interface tourne en `asInvoker`. Les opérations qui l'exigent passent par un processus *worker* élevé (UAC), lancé à la demande et piloté par un canal nommé protégé. |
| Liste blanche de sécurité | `SafetyPolicy` refuse toute action visant Defender, le pare-feu, Windows Update, BitLocker, VBS/HVCI, Secure Boot, les mitigations CPU, les services critiques, etc. Seules exceptions, listées une à une (`SecurityTradeOffRegistry`, `SecurityTradeOffHandlers`) et imposées par la validation du catalogue au niveau Extrême uniquement, avec consentement et réversibilité : la valeur HVCI `Enabled`, le plafond processeur des analyses planifiées et l'exclusion des dossiers de jeux (interface officielle de Defender). Vérifié à la compilation du catalogue **et** dans le worker. |
| Aucune valeur inventée | Une donnée non lisible est affichée « indéterminée » avec la raison (droits, capteur absent, API indisponible). |
| Local et sans télémétrie | Aucune requête réseau sans action explicite de l'utilisateur. |

## 2. Découpage en modules

```
FrameCore.slnx
├── src/
│   ├── FrameCore.Core                 Modèles, catalogue, moteur transactionnel, élévation, plateforme (registre, WMI, PDH, outils système)
│   ├── FrameCore.HardwareDetection    Scanner matériel (CPUID, topologie, WMI, DXGI, D3DKMT, TPM, firmware, ReBAR), capteurs (LibreHardwareMonitor)
│   ├── FrameCore.WindowsOptimization  Exécuteurs d'actions : registry, service, task, powerSetting, powerScheme, powerOverlay, bcdRemove, spi, dxSetting, optionalFeature, netAdapterProperty, handler
│   ├── FrameCore.StartupManager       Inventaire des programmes au démarrage (Run, StartupApproved, dossiers, tâches, services tiers) + base de connaissances
│   ├── FrameCore.CpuManager           Surveillance CPU, test de stabilité (vérification des calculs), conseils AMD/Intel
│   ├── FrameCore.GpuManager           NVML (NVIDIA), ADLX (AMD), réglage manuel / automatique, test de stabilité D3D11
│   ├── FrameCore.MemoryManager        Analyse de configuration RAM (canaux, XMP/EXPO, références), suivi mémoire par processus
│   ├── FrameCore.DriverManager        Inventaire des pilotes, signataires, mises à jour Windows Update, pages officielles
│   ├── FrameCore.BiosManager          Conseils firmware (version, date, Secure Boot, ReBAR), liens constructeur officiels
│   ├── FrameCore.StorageManager       Disques (NVMe/SSD/HDD, SMART, TRIM), nettoyage, retrim, fichier d'échange, hibernation
│   ├── FrameCore.NetworkManager       Interfaces réseau, test de latence local, autotuning TCP
│   ├── FrameCore.DisplayManager       Écrans (QueryDisplayConfig, EDID, VRR, HDR, fréquence maximale)
│   ├── FrameCore.BenchmarkManager     Capture ETW des présentations, PresentMon, statistiques de frametimes, comparaison statistique, détecteur de goulot,
│   │                                  QuickBench (benchmark rapide, laboratoire, statistiques), Verification (vérification & réparation),
│   │                                  Games (bibliothèque de 91 jeux, tables matériel, estimateur de FPS)
│   ├── FrameCore.BackupManager        Journal des modifications (JSON atomique), export/import, points de restauration
│   └── FrameCore.UI                   Application WPF (exécutable FrameCore.exe), vues, ViewModels, services, worker élevé
├── catalog/
│   ├── tweaks/*.json                  Catalogue d'optimisations
│   └── knowledge/*.json               Versions de Windows, règles de classification du démarrage
├── lang/fr.json, lang/en.json         Textes de l'interface
├── tests/FrameCore.Tests              Tests unitaires xUnit (logique indépendante de Windows)
├── tests/FrameCore.WindowsIntegrationTests  Tests sur Windows réel (CI, machine jetable uniquement) : scan matériel, appliquer/restaurer tout le catalogue
├── .github/workflows/windows-ci.yml   CI : installateur (Linux) + tests, intégration, visite automatique de l'interface et parcours utilisateur, installation/désinstallation (Windows)
├── build/                             Scripts de build (bash, PowerShell)
└── installer/                         Script NSIS
```

Les bibliothèques ciblent `net10.0` (et non `net10.0-windows`) afin que la logique pure soit testable sur
n'importe quelle plateforme ; les appels Windows sont isolés derrière des interfaces (`IRegistryAccess`,
`IServiceConfigurator`, `ITaskScheduler`, `IPowerAccess`…) qui disposent d'implémentations en mémoire pour les tests.

## 3. Catalogue d'optimisations

Chaque entrée (`TweakDefinition`) contient :

- `id`, `category`, `name`/`description`/`impact`/`regressions`/`mechanism` (FR + EN) ;
- `confidence` : `Confirmed`, `Probable`, `Experimental`, `NotRecommended` ;
- `risk` : `None` … `High` ; `restart` : `None`, `SignOut`, `Reboot`, `AppRestart` ;
- `profiles` : niveaux de profil simple qui l'incluent ;
- `conditions` : prédicats sur les *faits machine* (`MachineFacts`) — par exemple `cpu.dualCcdX3D == false`, `os.build >= 22621`, `system.laptop == false` ;
- `actions` : liste ordonnée d'actions typées ;
- `reversible`, `requiresConsent`, `sources`.

Règles vérifiées au chargement (`TweakCatalog.Validate`) et par les tests :
un réglage `NotRecommended` n'appartient à aucun profil ; un réglage `Experimental` n'apparaît que dans le
profil le plus poussé ; le profil le plus léger ne contient que des réglages confirmés/probables à risque faible ;
aucune action ne touche un composant protégé ; seuls certains éléments BCD peuvent être *supprimés* (jamais forcés).

### Types d'actions

| Type | Effet | Restauration |
|---|---|---|
| `registry` | Écrit une valeur (HKCU, HKLM) | Valeur et type d'origine, ou suppression si absente |
| `dxSetting` | Modifie une clé de `DirectXUserGlobalSettings` sans écraser les autres | Clé d'origine |
| `service` | Change le type de démarrage d'un service **de la liste autorisée** | Type d'origine |
| `task` | Active/désactive une tâche planifiée | État d'origine |
| `powerSetting` | Change un paramètre du plan d'alimentation actif (AC/DC) | Valeurs d'origine |
| `powerScheme` | Active un plan (en le créant par duplication si nécessaire) | Plan actif d'origine |
| `powerOverlay` | Mode d'alimentation Windows 11 (curseur) | Overlay d'origine |
| `bcdRemove` | **Supprime** une option BCD forcée (ex. `useplatformclock`) | Valeur d'origine réécrite |
| `spi` | `SystemParametersInfo` (accélération souris, animations) | Valeurs d'origine |
| `optionalFeature` | Fonctionnalité Windows facultative (DISM) | État d'origine |
| `netAdapterProperty` | Propriété avancée d'une carte réseau physique | Valeur d'origine |
| `handler` | Opération codée en C# et nommée (nettoyage, retrim, fichier d'échange automatique…) | Selon l'opération |

### Compatibilité des restaurations entre versions

Une modification doit rester restaurable aussi longtemps que FrameCore est installé, même par une version plus récente.
Règle pour faire évoluer le catalogue : **ne jamais supprimer ni modifier une action publiée** — l'ancienne action passe
dans `retiredActions` de la fiche (ou la fiche entière dans `catalog/retired.json` si l'optimisation disparaît). Le
processus élevé accepte ces actions pour lire, restaurer et vérifier, jamais pour appliquer ; l'opération contrôlée est
celle de la route appelée, pas celle écrite dans la requête. `tests/FrameCore.Tests/fixtures/restorable-actions-*.json`
fige les actions de chaque version publiée (test `LongTermRestoreTests`).

## 4. Moteur transactionnel

```
Evaluate(tweak)  →  conditions, lecture de l'état courant de chaque action, statut (Applied / NotApplied / Partial / NotApplicable)
Apply(tweaks)    →  pour chaque réglage :
                      1. Capture de l'état réel de chaque action (StateSnapshot)
                      2. Écriture du ChangeSet en état Pending dans le journal (fichier JSON atomique)
                      3. Application action par action
                      4. En cas d'échec : restauration en ordre inverse des actions déjà appliquées → état Failed/RolledBack
                      5. Vérification (relecture) → état Applied + Verified
Restore(set)     →  réécrit chaque StateSnapshot (valeur absente → suppression), vérifie, marque Restored
```

Au démarrage, un ChangeSet resté en `Pending` (coupure de courant, plantage) est signalé comme `Failed` pour être
restauré manuellement : il n'est jamais rejoué automatiquement.

## 5. Séparation des privilèges

```
FrameCore.exe (utilisateur standard)                      FrameCore.exe --worker <pipe> <pid> (administrateur)
  │  ElevationBroker : ShellExecute "runas" (UAC visible)  ──►  vérifie que le parent est le même exécutable
  │  NamedPipeServer (ACL : utilisateur + Administrateurs)  ◄──  se connecte au canal
  │  vérifie le PID client (GetNamedPipeClientProcessId)
  │  messages JSON préfixés par leur longueur
  └─ PrivilegedRequestValidator côté worker :
       - action du catalogue : doit correspondre exactement (forme canonique) à une action d'un réglage connu
       - action dynamique : seulement les origines « startup », « service tiers », « tâche » avec contraintes strictes
       - SafetyPolicy appliquée à nouveau
```

Le worker ne reçoit jamais de ligne de commande ni de script : uniquement des opérations nommées
(`action.apply`, `action.restore`, `gpu.apply`, `restorepoint.create`, `bench.etw`…). Les écritures HKCU sont
redirigées vers `HKEY_USERS\<SID de l'utilisateur de l'interface>` pour ne pas modifier le profil de l'administrateur
lorsque l'élévation se fait avec un autre compte.

Les outils système autorisés sont limités à `System32` (bcdedit, powercfg, netsh, dism, defrag, mdsched, cleanmgr),
lancés avec `ArgumentList` (aucune interprétation par un shell) et des arguments filtrés.

## 6. Détection matérielle

| Domaine | Sources |
|---|---|
| CPU | CPUID (X86Base), `GetLogicalProcessorInformationEx` (cœurs P/E, groupes L3 → CCD), WMI `Win32_Processor` |
| GPU | DXGI (adaptateurs, VRAM), D3DKMT (HAGS, type d'adaptateur), WMI, NVML/ADLX si présents |
| RAM | WMI `Win32_PhysicalMemory` (vitesse configurée en MT/s, référence), décodage des références de kits, SPD via LibreHardwareMonitor si PawnIO est installé |
| Carte mère / BIOS | WMI `Win32_BaseBoard`, `Win32_BIOS`, `GetFirmwareType`, `Tbsi_GetDeviceInfo`, état Secure Boot |
| Stockage | `MSFT_PhysicalDisk`, `MSFT_StorageReliabilityCounter` (droits administrateur), TRIM via `DisableDeleteNotification` |
| Écrans | `QueryDisplayConfig`, EDID (parseur interne), HDR/VRR annoncés |
| Windows | Registre `CurrentVersion`, base de versions `windows-versions.json`, VBS/HVCI (`Win32_DeviceGuard`) |

## 7. Benchmark

- **Capture en jeu** : session ETW temps réel sur les fournisseurs DXGI (Present_Start) et D3D9 ; ou Intel PresentMon
  (exécutable signé Intel vérifié par Authenticode) ; ou import d'un CSV PresentMon.
- **Statistiques** : FPS moyen, 1 % low (moyenne des 1 % d'images les plus lentes), 0,1 % low seulement au-delà de
  10 000 images, P95/P99, écart-type, saccades.
- **Comparaison** : test t de Welch entre sessions répétées, avec seuil de bruit de 2 % ; une différence dans le bruit
  est présentée comme « aucun gain démontré ».
- **Détecteur de goulot** : règles appliquées aux mesures capturées (utilisation GPU/CPU par cœur, fréquence, VRAM).

## 8. Données et persistance

| Données | Emplacement |
|---|---|
| Application, catalogue, langues, documentation | Dossier d'installation (lecture seule) |
| Réglages, journal des modifications, sauvegardes, benchmarks, rapports, journaux | `%LOCALAPPDATA%\FrameCore` |

L'installateur ne contient jamais de données propres à une machine : chaque installation réalise sa propre détection.

## 9. Vérification & réparation, benchmark rapide, estimation des FPS (version 2)

```
TweakService.ApplyAsync ─► lot enregistré (réglages prévus) ─► point de restauration ─► Engine.Apply (relecture après écriture)
        └─► VerifyRepairService.VerifyAndRepair : relit, réécrit ≤ 2 fois, restaure sinon ─► rapport (Verification/*.json)
OptimizationViewModel (option « mesurer ») : QuickBench express avant ─► application ─► QuickBench express après
        └─► VerifyRepairService.CheckPerformance : régression confirmée ─► restaurer les réglages liés ─► contre-mesure ─► garder ou réappliquer une fois
GamesViewModel : GameLibrary + FpsEstimator(mesures en jeu) + QuickBenchComparer(deux derniers benchmarks) ─► 91 estimations + 3 jeux
```

Les détails (formules, seuils, répétitions) sont dans [BENCHMARKS.md](BENCHMARKS.md).

## 10. Interface (version 2)

- Système de design : `Themes/Colors.*.xaml`, `Tokens.xaml`, `Icons.xaml`, `Styles.xaml`, `Templates.xaml` — voir [DESIGN.md](DESIGN.md).
- Contrôles dessinés : `Infrastructure/Controls.cs` (icône vectorielle, jauge annulaire), `Charts.cs` (courbes), `Motion.cs`
  (transitions, entrées, comptage, barres ; respect du réglage Windows des animations).
- Navigation : Tableau de bord ; Performances (Optimisation, Jeux & FPS, Benchmark) ; Système (Matériel, Pilotes, Overclocking) ;
  Contrôle (Restauration, Mode Pro, Paramètres).
- `FrameCore.exe --ui-tour <dossier>` : visite automatique de chaque écran (captures PNG, erreurs de liaison, temps de rendu,
  mémoire, CPU au repos, profil détecté) ; sur une machine jetable (`FRAMECORE_DISPOSABLE_VM=1`), exécute aussi les parcours
  benchmark → optimisation mesurée → vérification → restauration.
