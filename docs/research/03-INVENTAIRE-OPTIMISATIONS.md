# Inventaire technique des optimisations

Ce document est l'analyse humaine. La **source de vérité exécutable** est le catalogue
`catalog/tweaks/*.json` (chargé par l'application, testé par `CatalogTests`). Chaque entrée
appliquable du catalogue porte le même identifiant que ci-dessous.

## Échelle utilisée

**Confiance**
- **Confirmé** : documentation officielle ou bénéfice reproductible et compris.
- **Probable** : bénéfice plausible dans certaines configurations, mécanisme connu.
- **Expérimental** : résultats variables, non établis → jamais appliqué automatiquement, validation spécifique.
- **Non recommandé** : sans bénéfice établi, obsolète, placebo ou trop risqué → non appliqué, expliqué.

**Nature** (pour ne pas confondre confort et FPS) : `performance`, `latence`, `réactivité`
(bureau/menus, pas de FPS), `arrière-plan` (réduit l'activité de fond), `confort`, `confidentialité`,
`réparation` (annule un mauvais réglage antérieur), `maintenance`, `stabilité`.

**Redémarrage** : aucun / fermeture de session / redémarrage Windows / redémarrage de l'Explorateur.

Toutes les optimisations appliquables sont **réversibles à la valeur réellement capturée**, sauf les
opérations de maintenance sans état (nettoyage, ReTrim) indiquées comme telles.

---

## 1. Jeu et graphismes Windows

| ID | Réglage (mécanisme) | Impact attendu | Configurations | Régressions / risques | Confiance | Profils | Redém. |
|---|---|---|---|---|---|---|---|
| `gaming.gamemode` | Mode Jeu : `HKCU\Software\Microsoft\GameBar` `AutoGameModeEnabled=1`, `AllowAutoGameMode=1` | Windows évite les installations de pilotes WU et notifications de redémarrage pendant le jeu, priorise le jeu | Toutes | Quasi nul (activé par défaut) | Confirmé | L/É/M | Non |
| `gaming.dvr.background` | Enregistrement en arrière-plan : `HKCU\...\GameDVR` `HistoricalCaptureEnabled=0` | Supprime l'encodage vidéo permanent (encodeur GPU, disque) **s'il était activé** | Toutes | Perte de « Enregistrer ce qui vient de se passer » | Confirmé | L/É/M | Non |
| `gaming.dvr.capture` | Captures Game Bar : `AppCaptureEnabled=0`, `HKCU\System\GameConfigStore` `GameDVR_Enabled=0` | Réduit les hooks de capture ; gain FPS non démontré | Sauf Ryzen X3D bi-CCD (la Game Bar sert à la détection des jeux) | Plus de capture Win+Alt+R | Probable | É/M | Non |
| `graphics.windowed` | Optimisations pour les jeux fenêtrés : `DirectXUserGlobalSettings` → `SwapEffectUpgradeEnable=1;` | Modèle de présentation *flip* pour DX10/11 fenêtré/sans bordure : latence réduite, VRR/Auto HDR possibles (doc Microsoft) | Windows 11 22H2+ | Rares incompatibilités de jeux anciens (désactivable par jeu) | Confirmé | L/É/M | Non |
| `graphics.vrr` | Taux d'actualisation variable Windows : `VRROptimizeEnable=1;` | Active la VRR pour les jeux DX11 plein écran qui ne la gèrent pas | Écran VRR détecté | Scintillement sur certaines dalles à faible FPS | Probable | É/M | Non |
| `graphics.hags` | Planification GPU accélérée matériellement : `HKLM\...\GraphicsDrivers` `HwSchMode=2` | Réduit l'overhead CPU de planification ; **requis** pour DLSS Frame Generation | Supporté par le GPU+pilote (D3DKMT WDDM 2.7 caps) | Incompatibilités ponctuelles (VR, captures) | Probable | É/M | Oui |
| `graphics.mpo.disable` | MPO : `HKLM\SOFTWARE\Microsoft\Windows\Dwm` `OverlayTestMode=5` | **Aucun gain de FPS** ; corrige parfois un scintillement | Dépannage uniquement | Désactive les plans de superposition (peut augmenter la latence/consommation) | Non recommandé | Pro | Oui |
| `graphics.autohdr` | Auto HDR : `AutoHDREnable=1;` | Visuel (confort), pas de FPS | Écran HDR | Coût GPU léger | — (confort) | Pro | Non |
| *(par application)* | Préférence GPU : `UserGpuPreferences\<exe>` = `GpuPreference=2;` | Évite qu'un jeu tourne sur l'iGPU d'un portable hybride | Portables iGPU+dGPU | — | Confirmé | Pro (GPU) | Non |
| — | Optimisations plein écran (désactiver par exe) | Historiquement utile, aujourd'hui le flip indépendant offre la même latence | — | — | Non recommandé (obsolète) | — | — |

## 2. Effets visuels et interface

| ID | Réglage | Impact | Risques | Confiance | Profils | Redém. |
|---|---|---|---|---|---|---|
| `visual.transparency` | `Themes\Personalize` `EnableTransparency=0` | Réactivité du bureau sur iGPU modeste ; nul en jeu plein écran | Esthétique | Probable (réactivité) | L/É/M | Non |
| `visual.animations` | `SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, FALSE)` | Supprime les délais d'animation (réactivité perçue) | Esthétique | Confirmé (réactivité) | L/É/M | Non |
| `visual.menudelay` | `Control Panel\Desktop` `MenuShowDelay=200` (défaut 400) | Menus plus rapides | — | Confirmé (réactivité) | É/M | Session |
| `content.suggestions` | `ContentDeliveryManager` `SubscribedContent-338388/338389/353694/353696Enabled=0`, `SoftLandingEnabled=0`, `SystemPaneSuggestionsEnabled=0` | Moins de contenus promotionnels | — | Confirmé (confort) | L/É/M | Non |
| `content.silentapps` | `SilentInstalledAppsEnabled=0`, `ContentDeliveryAllowed`… | Empêche l'installation silencieuse d'applications promues (activité de fond) | — | Confirmé (arrière-plan) | L/É/M | Non |
| `widgets.disable` | Stratégie `HKLM\SOFTWARE\Policies\Microsoft\Dsh` `AllowNewsAndInterests=0` | Arrête le tableau Widgets (processus WebView2) ; RAM | Perte des widgets | Probable (arrière-plan) | É/M | Session |
| `search.websuggestions` | Stratégie `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions=1` | Pas de recherche web dans Démarrer | — | Confirmé (confort/confidentialité) | É/M | Explorateur |
| `notifications.scoobe` | `UserProfileEngagement` `ScoobeSystemSettingEnabled=0` | Supprime les écrans « terminer la configuration » | — | Confirmé (confort) | L/É/M | Non |

## 3. Arrière-plan, confidentialité, services, tâches

| ID | Réglage | Impact | Risques | Confiance | Profils |
|---|---|---|---|---|---|
| `privacy.backgroundapps` | `BackgroundAccessApplications` `GlobalUserDisabled=1` | Les applications du Store ne tournent plus en fond | Notifications d'apps Store (Courrier, Teams nouvelle version, Mobile connecté) | Probable (arrière-plan) | É/M |
| `privacy.adid` | `AdvertisingInfo` `Enabled=0` | Confidentialité, pas de FPS | — | Confirmé (confidentialité) | É/M |
| `privacy.tailored` | `Privacy` `TailoredExperiencesWithDiagnosticDataEnabled=0` | Confidentialité | — | Confirmé (confidentialité) | É/M |
| `privacy.telemetry` | Stratégie `DataCollection` `AllowTelemetry=1` (données requises ; 0 n'est effectif qu'en Entreprise/Éducation) | Moins de données de diagnostic ; gain de performance non démontré | — | Confirmé (confidentialité) | É/M |
| `privacy.recall` | Stratégie `WindowsAI` `DisableAIDataAnalysis=1` (24H2+) | Arrête les captures Recall (disque/NPU) | Perte de Recall | Probable (arrière-plan) | É/M |
| `svc.diagtrack` | Service `DiagTrack` → Désactivé | Arrête l'envoi de télémétrie (activité disque/réseau ponctuelle) | Programme Insider | Probable (arrière-plan) | M |
| `svc.mapsbroker` | `MapsBroker` → Manuel | Négligeable | — | Probable (arrière-plan) | M |
| `svc.trkwks` | `TrkWks` → Manuel | Négligeable | Suivi des raccourcis réseau | Probable (arrière-plan) | M |
| `svc.pcasvc` | `PcaSvc` → Manuel | Négligeable | Assistant de compatibilité des programmes | Probable (arrière-plan) | M |
| `svc.spooler` | `Spooler` → Manuel **si aucune imprimante physique** | Négligeable en FPS ; réduit la surface d'attaque | Impression impossible jusqu'à restauration | Probable | M |
| `svc.sysmain` | `SysMain` → Manuel | Pas de gain FPS démontré ; peut **ralentir** les lancements | Plus de préchargement | Expérimental | Pro |
| `svc.wsearch` | `WSearch` → Manuel | Réduit l'indexation après de gros changements de fichiers (Windows réduit déjà l'indexation pendant l'activité) | Recherche lente dans Démarrer/Outlook | Expérimental | Pro |
| `task.appraiser` | Tâche `\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser` désactivée | Supprime les pics CPU de CompatTelRunner | Évaluation de compatibilité des mises à jour de fonctionnalités | Probable (arrière-plan) | M |
| `task.ceip` | `Customer Experience Improvement Program\Consolidator` et `UsbCeip` | Négligeable | — | Probable (arrière-plan) | M |
| `task.diskdiag` | `DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector` | Négligeable | — | Probable (arrière-plan) | M |
| `task.feedback` | `Feedback\Siuf\DmClient`, `DmClientOnScenarioDownload` | Négligeable | — | Probable (arrière-plan) | M |
| `task.maps` | `Maps\MapsUpdateTask` | Négligeable | — | Probable (arrière-plan) | M |
| — | Services Xbox (`XblAuthManager`, `XboxGipSvc`…) | Déjà en démarrage à la demande ; nécessaires au Game Pass et aux manettes | Casse les jeux Xbox/manettes | Non recommandé | — |
| — | Désactiver Windows Update / Defender / pare-feu / BitLocker | — | Sécurité | **Interdit** | — |

## 4. Alimentation et processeur

| ID | Réglage (powrprof) | Impact | Configurations | Risques | Confiance | Profils | Redém. |
|---|---|---|---|---|---|---|---|
| `power.repair.boost` | Réparation : `PERFBOOSTMODE` (AC) remis à *Agressif* s'il a été désactivé | Restaure le boost CPU | Toutes | — | Confirmé (réparation) | L/É/M | Non |
| `power.repair.maxstate` | Réparation : `PROCTHROTTLEMAX` (AC) remis à 100 % | Restaure la fréquence max | Toutes | — | Confirmé (réparation) | L/É/M | Non |
| `power.plan.high` | Plan « Performances élevées » | État minimal 100 %, pas de parcage : montée en fréquence plus rapide ; gain variable | PC fixe, hors Ryzen X3D bi-CCD, hors AMD (AMD recommande Équilibré sous Windows 11) | Consommation au repos | Probable | É/M | Non |
| `power.plan.ultimate` | Plan « Performances optimales » (`e9a42b02-…`), dupliqué si absent | Supprime des micro-latences de gestion d'énergie (description Microsoft, orientée stations de travail) ; gain jeu non démontré | PC fixe, hors X3D bi-CCD | Consommation, chaleur | Expérimental | M (exp.) | Non |
| `power.overlay.best` | Mode d'alimentation Windows 11 « Meilleures performances » (API non documentée `PowerSetActiveOverlayScheme`) | Équivalent du curseur des Paramètres | Portables/Modern Standby | API non documentée | Probable | É/M | Non |
| `power.usb.suspend` | `USBSELECTIVESUSPEND` (AC) = désactivé | Évite la mise en veille de périphériques USB (souris, DAC) | Toutes (secteur) | Consommation USB | Probable (latence) | É/M | Non |
| `power.pcie.aspm` | `ASPM` (AC) = désactivé | Évite la latence de sortie de L1 des liens PCIe | PC fixe | Consommation | Probable (latence) | É/M | Non |
| `power.disk.timeout` | Extinction disque (AC) = jamais | Évite les réveils de HDD (saccades) | Présence de HDD | Usure/consommation | Probable | É/M | Non |
| `power.minstate` | `PROCTHROTTLEMIN` (AC) = 100 % | Garde la fréquence haute ; peut **réduire** le boost (chaleur) | — | Chaleur | Expérimental | Pro | Non |
| `power.coreparking` | `CPMINCORES` (AC) = 100 % | Désactive le parcage | **Interdit** sur X3D bi-CCD et Intel hybride (parcage voulu) | Consommation | Expérimental | Pro | Non |
| `power.epp` | `PERFEPP` (AC) = 0 | Préférence performance maximale (HWP/CPPC) | CPU avec HWP/CPPC | Chaleur | Expérimental | Pro | Non |
| `power.fastboot` | Démarrage rapide `HiberbootEnabled=0` | Noyau neuf à chaque démarrage (stabilité pilotes) ; démarrage plus lent | Toutes | Démarrage plus long | Probable (stabilité) | Pro | Oui |

## 5. Planification, latence, timers

| ID | Réglage | Analyse | Confiance | Profils |
|---|---|---|---|---|
| `sched.priosep.38` | `Win32PrioritySeparation=0x26` | Décodage : intervalle court (10), variable (01), boost 3:1 (10). La valeur par défaut **2** (00 00 10) signifie sur un Windows client : intervalle court, variable, boost 3:1 → **identique**. Placebo. | Non recommandé | Pro (informatif) |
| `mmcss.responsiveness` | `SystemResponsiveness=10` | Ne concerne que les threads enregistrés MMCSS. **0 est ramené à 20** (doc Microsoft) ; 10 est le minimum effectif | Expérimental | Pro |
| `mmcss.networkthrottling` | `NetworkThrottlingIndex=0xFFFFFFFF` | Désactive la limitation réseau MMCSS pendant la lecture multimédia ; effet en jeu non démontré | Expérimental | Pro |
| `mmcss.gamestask` | `Tasks\Games` : `Priority=6`, `Scheduling Category=High`, `SFIO Priority=High` | Ne concerne que les jeux appelant `AvSetMmThreadCharacteristics("Games")` (rares) | Expérimental | Pro |
| `timer.global` | `kernel\GlobalTimerResolutionRequests=1` | Restaure la résolution de timer globale (pré-2004) ; coût énergétique ; les jeux demandent déjà leur propre résolution | Expérimental | Pro |
| `bcd.platformclock.remove` | Retrait de `useplatformclock` **s'il a été forcé** | HPET forcé = appel système + ~0,8–1 µs par QPC (doc Microsoft) → retour au choix automatique de Windows | Confirmé (réparation) | L/É/M |
| `bcd.dynamictick.remove` | Retrait de `disabledynamictick` s'il a été forcé | Retour au comportement par défaut ; effet latence du forçage non démontré | Expérimental | Pro |
| — | Forcer HPET / `useplatformtick` / `tscsyncpolicy` | Jamais sans justification propre à la machine | Non recommandé | — |
| — | Désactiver le périphérique HPET | Sans bénéfice établi sur Windows 10/11 | Non recommandé | — |
| — | MSI Mode (`MessageSignaledInterruptProperties\MSISupported`) | Les GPU/NVMe/cartes réseau récents utilisent déjà MSI/MSI-X ; forcer MSI sur un périphérique qui ne le supporte pas peut empêcher le démarrage | Non recommandé (affichage de l'état uniquement) | Pro (lecture) |
| — | Affinité des interruptions (Interrupt Affinity Policy) | Résultats non reproductibles, risque de dégradation | Non recommandé | — |
| — | Désactiver les mitigations Spectre/Meltdown (`FeatureSettingsOverride`) | Risque de sécurité majeur | **Interdit** | — |

## 6. Souris et périphériques

| ID | Réglage | Nature | Confiance | Profils |
|---|---|---|---|---|
| `input.mouse.accel` | `SPI_SETMOUSE {0,0,0}` (« Améliorer la précision du pointeur » désactivé) | Cohérence de visée (confort), **pas de FPS** ; sans effet dans les jeux en *raw input* | Confirmé (confort) | É/M |
| `input.stickykeys` | `Accessibility\StickyKeys` `Flags=506` (raccourci Maj×5 désactivé) | Confort en jeu | Confirmé (confort) | É/M |
| — | Fréquence d'interrogation (polling rate) | Uniquement via le logiciel du fabricant | — | Information |

## 7. Réseau

| ID | Réglage | Analyse | Confiance | Profils |
|---|---|---|---|---|
| `net.repair.autotuning` | Réparation : `autotuninglevel=normal` si désactivé | Désactiver l'auto-tuning TCP réduit le débit sans gain de ping | Confirmé (réparation) | L/É/M |
| `net.eee` | Ethernet : `*EEE=0` | Supprime les micro-latences de sortie de veille du lien (LPI) ; gain faible | Probable (latence) | É/M (Ethernet) |
| `net.intmod` | `*InterruptModeration=0` | Latence légèrement réduite, **CPU plus élevé** | Expérimental | Pro |
| — | Nagle / `TcpAckFrequency` / `TCPNoDelay` | Ne concerne que TCP ; la plupart des jeux utilisent UDP ; `TCPNoDelay` n'est pas lu par Windows moderne | Non recommandé | — |
| — | `NonBestEffortLimit=0` (« libérer 20 % de bande passante ») | Mythe : la réservation QoS ne s'applique qu'aux flux QoS | Non recommandé (placebo) | — |
| — | Changer de DNS pour « réduire le ping » | Le DNS n'intervient qu'à la connexion ; FrameCore mesure seulement la latence DNS | Non recommandé (pour le ping) | Diagnostic |
| — | MTU | Valeur automatique (PMTUD) ; modification manuelle source de fragmentation | Non recommandé | Diagnostic |

## 8. Stockage

| ID | Réglage | Analyse | Confiance | Profils |
|---|---|---|---|---|
| `storage.trim` | Réparation : `DisableDeleteNotification=0` | TRIM indispensable aux SSD | Confirmé (réparation) | L/É/M |
| `storage.optimize.task` | Réparation : tâche `Defrag\ScheduledDefrag` réactivée | Windows choisit ReTrim (SSD) ou défragmentation (HDD) | Confirmé (réparation) | L/É/M |
| `storage.retrim` | `defrag.exe <volume> /L` sur les SSD | Maintenance sans état | Confirmé (maintenance) | L/É/M |
| `cleanup.temp` | Suppression des fichiers temporaires de plus de 24 h (%TEMP%, Windows\Temp, rapports WER archivés) | Maintenance, libère de l'espace ; **jamais** les caches de shaders (leur suppression provoque des saccades) | Confirmé (maintenance) | L/É/M |
| `storage.pagefile.auto` | Réparation : fichier d'échange géré par le système s'il a été désactivé | Désactiver le fichier d'échange provoque des plantages par manque de mémoire engagée | Confirmé (réparation) | É/M |
| `storage.hibernate.off` | `powercfg /hibernate off` | Libère l'espace de hiberfil.sys ; désactive la veille prolongée et le démarrage rapide | Probable (espace) | Pro |
| — | Défragmentation classique d'un SSD | Inutile et use le SSD | **Interdit** | — |
| — | `NtfsDisableLastAccessUpdate=1` | Déjà géré par le système depuis Windows 10 1803 | Non recommandé (placebo) | — |
| — | Suppression du fichier d'échange | — | **Interdit** | — |

## 9. Mémoire

| Réglage | Analyse | Confiance |
|---|---|---|
| Vidage des listes Standby / « nettoyeur RAM » | La mémoire en attente est un cache récupérable instantanément ; la vider force des relectures disque. FrameCore **n'en propose pas**. | Non recommandé (faux optimiseur) |
| Désactiver la compression mémoire | Utile seulement avec beaucoup de RAM libre ; augmente la pagination sinon | Non recommandé |
| `DisablePagingExecutive=1`, `LargeSystemCache=1` | Sans bénéfice démontré sur systèmes modernes ; `LargeSystemCache` est destiné aux serveurs de fichiers | Non recommandé |
| XMP/EXPO | **Le gain mémoire le plus important** (souvent DDR4-2133 → 3200/3600, DDR5-4800 → 6000) ; activation dans le **BIOS** uniquement | Confirmé (guidage BIOS) |
| Bicanal | Une seule barrette = moitié de bande passante → impact fort sur les 1 % low (iGPU surtout) | Confirmé (diagnostic) |

## 10. Sécurité ayant un coût de performance (analyse uniquement)

| Fonction | Analyse FrameCore | Action |
|---|---|---|
| Intégrité de la mémoire (HVCI) | Microsoft reconnaît un impact possible sur certaines configurations. Sans **MBEC/GMET** (Intel avant Kaby Lake, AMD avant Zen 2) l'émulation coûte davantage : FrameCore le signale via `Win32_DeviceGuard`. | Explication + bouton vers *Sécurité Windows → Isolation du noyau*. **Jamais automatique.** |
| Plateforme de machine virtuelle (VMP) | Fonctionnalité facultative ; Microsoft cite un impact possible. Nécessaire à WSL2, Android, certaines VM. | Désactivation proposée dans le profil Maximal **avec consentement explicite** (`security.vmp`) ; réactivation par restauration |
| Defender, pare-feu, Windows Update, Secure Boot, TPM, BitLocker | Indispensables | **Jamais modifiés** |

## 11. Matériel et BIOS (guidage)

| Vérification | Méthode | Action |
|---|---|---|
| Fréquence d'écran < maximum | `EnumDisplaySettingsEx` | Proposition de la fréquence max avec confirmation et retour automatique après 15 s sans validation |
| Limitation câble/port | Comparaison plage EDID / modes disponibles + type de connexion | Information |
| XMP/EXPO probablement inactif | `ConfiguredClockSpeed` vs vitesse décodée de la référence | Instructions BIOS adaptées au fabricant |
| Resizable BAR | Taille de la fenêtre mémoire PCI du GPU (et BAR1 NVML) | Instructions BIOS (Above 4G Decoding + ReBAR/CSM off) |
| Lien PCIe du GPU dégradé (largeur) | NVML | Information (slot, riser) |
| BIOS ancien | Date SMBIOS | Lien vers la page support officielle ; jamais de flash automatique |
| Ryzen X3D bi-CCD | Topologie L3 + modèle | Vérification pilote chipset AMD, Game Bar, plan Équilibré |

## 12. Overclocking

| Constructeur | Méthode | Disponibilité |
|---|---|---|
| NVIDIA | NVML `nvmlDeviceSetClockOffsets` (offsets cœur/mémoire bornés par min/max renvoyés par le pilote) et `nvmlDeviceSetPowerManagementLimit` (borné par les contraintes) ; recherche par paliers + test de stabilité + retour au dernier palier stable | Implémenté ; **test sur matériel réel à effectuer** |
| AMD Radeon | ADLX `IADLXGPUAutoTuning` (undervolt GPU / OC GPU / OC VRAM : profils officiels AMD) ; `IADLXManualPowerTuning` (limite de puissance bornée) ; `ResetToFactory` | Implémenté ; **test sur matériel réel à effectuer** |
| Intel Arc | — | Indisponible (guidage Intel Graphics Software) |
| CPU AMD | Aucune API publique de contrôle | Guidage Ryzen Master / BIOS (PBO, Curve Optimizer) |
| CPU Intel | Aucune API publique de contrôle | Guidage XTU (7.14 / 10.x) / BIOS ; détection K/KF/KS + chipset Z |
| Tension | **Jamais augmentée** par FrameCore | — |
