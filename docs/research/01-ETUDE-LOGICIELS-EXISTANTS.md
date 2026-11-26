# Étude des logiciels existants

> Recherche effectuée le 26/11/2026. Les constats ci-dessous proviennent de la documentation
> officielle des éditeurs, du code source public lorsque disponible (WinUtil, PresentMon,
> LibreHardwareMonitor, ADLX, NVML) et, à défaut, de sources secondaires signalées comme telles.

## Synthèse

| Logiciel | Ce qu'il fait bien | Limites / risques relevés | Ce que FrameCore reprend | Ce que FrameCore refuse |
|---|---|---|---|---|
| **Chris Titus Tech WinUtil** | Catalogue JSON lisible (`config/tweaks.json`), séparation « Essential / Advanced », point de restauration, interface unique | La restauration remet une **valeur codée en dur** (`OriginalValue`) et non la valeur réellement présente sur la machine ; certains réglages « Essential » sont discutables (désactivation de BitLocker, suppression d'Edge/OneDrive en avancé) ; exécution de scripts PowerShell arbitraires (`InvokeScript`) ; aucune mesure d'impact | Le principe d'un catalogue déclaratif externe au code | Restauration par valeur supposée ; désactivation de BitLocker ; scripts libres |
| **AtlasOS** | Playbook complet appliqué via AME Wizard, documentation de chaque changement | Modifie Windows en profondeur (options pour désactiver Defender, les mitigations CPU, la virtualisation de sécurité, les mises à jour) ; non supporté par Microsoft ; retour arrière difficile hors réinstallation | L'idée de documenter chaque changement | Toute désactivation de protection de sécurité ; modification non réversible |
| **O&O ShutUp10++** | Classement par niveau de recommandation (vert/jaune/rouge), principalement des stratégies documentées, export de configuration | Orienté confidentialité, pas performance | Le classement par niveau de recommandation, l'usage préférentiel des **stratégies de groupe documentées** | — |
| **Razer Cortex** | « Boost » qui ferme des processus pendant le jeu, interface grand public | Fermetures de processus peu transparentes ; « nettoyage RAM » à l'efficacité contestée ; compte Razer | Simplicité du mode grand public | Faux optimiseur RAM, fermeture aveugle de processus |
| **Process Lasso (Bitsum)** | ProBalance (abaissement dynamique de priorité des processus gourmands), règles persistantes d'affinité/priorité, « Bitsum Highest Performance » | Logiciel résident ; les gains de ProBalance dépendent de la charge ; plan d'alimentation agressif → consommation au repos | Analyse par processus, détection des consommations anormales | Plan d'alimentation forcé sans condition (incompatible notamment avec les Ryzen X3D bi-CCD) |
| **MSI Afterburner / RivaTuner** | Référence de l'overclocking GPU, courbe tension/fréquence, OC Scanner (NVIDIA) | Utilise des interfaces constructeur non publiques ; développement historiquement incertain | Monitoring, principe d'un scan automatique validé par test de stabilité | Accès bas niveau non documenté |
| **NVIDIA App** | Tuning automatique officiel (scan de la courbe V/F), mises à jour pilote, réglages pilote | Fermé, pas d'API publique pour déclencher le tuning automatique | Redirection vers l'outil officiel ; FrameCore utilise **NVML** (API documentée) | — |
| **AMD Software: Adrenalin** | Auto Undervolt / Auto Overclock GPU/VRAM, réglages manuels, exposés via **ADLX** (SDK public) | — | Appel des fonctions d'auto-tuning **officielles** via ADLX | — |
| **AMD Ryzen Master** | PBO, Curve Optimizer, Auto OC, profils | **Aucune API publique de contrôle** (le *Monitoring SDK* est en lecture seule et nécessite le pilote Ryzen Master) | Détection + guidage vers l'outil officiel / le BIOS | Réimplémentation des écritures SMU/MSR |
| **Intel XTU** | Réglages de ratio, limites de puissance, undervolt (si non verrouillé) | Deux branches : 7.14 (jusqu'à la 14e génération) et 10.x (Core Ultra 200S, nécessite Intel IPF + support BIOS) ; pas de SDK public actuel | Détection de compatibilité + guidage | Écriture directe des MSR |
| **Intel PresentMon** (MIT) | Référence de la capture de frametimes (ETW), version 2.4.x avec CLI CSV | Exige des droits administrateur pour la session ETW | Import CSV PresentMon + capture ETW intégrée (métrique *MsBetweenPresents*) + pilotage de la CLI si l'utilisateur l'a installée | — |
| **Sysinternals Autoruns** | Exhaustivité des points de démarrage (Run, services, tâches, extensions shell, pilotes, Winlogon…), vérification de signature | Interface experte, désactivation = suppression ou déplacement selon le type | Inventaire multi-sources, colonne éditeur/signature, désactivation **réversible** (StartupApproved, Shell Extensions\Blocked, état des tâches/services) | Suppression physique d'entrées |
| **LibreHardwareMonitor** (MPL-2.0) | Capteurs CPU/GPU/carte mère/stockage/RAM, bibliothèque NuGet | Depuis 2025, **WinRing0 a été remplacé par PawnIO** (WinRing0 est signalé par Microsoft Defender, CVE-2020-14979). La version 0.9.6 (février 2026) intègre des modules PawnIO signés et accède au périphérique `\\?\GLOBALROOT\Device\PawnIO` uniquement s'il est **installé séparément** | Bibliothèque intégrée pour les capteurs avancés, en mode élevé, **seulement si PawnIO est présent** | Embarquer ou installer silencieusement un pilote noyau |

## Enseignements retenus pour FrameCore

1. **Restaurer la valeur réellement capturée**, jamais une valeur « par défaut » supposée
   (défaut principal relevé dans WinUtil). Une valeur absente est restaurée par suppression.
2. **Pas de scripts libres** : chaque action est un type fermé (registre, service, tâche,
   paramètre d'alimentation, élément BCD, gestionnaire codé) avec liste blanche.
3. **Pas de protection de sécurité désactivée automatiquement** (Defender, pare-feu, Windows Update,
   BitLocker, Secure Boot, TPM, intégrité de la mémoire). Les fonctions de sécurité ayant un coût
   documenté (intégrité de la mémoire, plateforme de machine virtuelle) sont **analysées et expliquées** ;
   l'utilisateur est redirigé vers la page Windows Security correspondante.
4. **Mesurer avant de prétendre** : le module Benchmark compare des sessions répétées et signale
   quand l'écart est inférieur à la variabilité mesurée.
5. **Overclocking uniquement par API officielle** : NVML (NVIDIA), ADLX (AMD). Sinon, guidage
   vers l'outil constructeur (Ryzen Master, XTU, Intel Graphics Software) ou le BIOS.
6. **Capteurs bas niveau** : jamais de WinRing0. LibreHardwareMonitor est utilisé tel quel ; il ne
   lit les capteurs CPU/carte mère que si le pilote signé **PawnIO** est installé par l'utilisateur.
   Sans lui, FrameCore affiche « Indéterminé » et utilise des sources sans pilote (compteurs PDH,
   NVML, ADLX, WMI).

## Points de vigilance spécifiques découverts

- **Ryzen X3D à deux CCD (7900X3D, 7950X3D, 9900X3D, 9950X3D…)** : le pilote « AMD 3D V-Cache
  Performance Optimizer » s'appuie sur la détection de jeu de la Xbox Game Bar et sur le plan
  d'alimentation **Équilibré** pour parquer le CCD sans cache. Basculer sur « Performances élevées »
  ou « Performances optimales » (comme le font beaucoup d'optimiseurs) peut empêcher ce parcage.
  FrameCore **bloque** ces réglages sur ces processeurs et vérifie à la place la présence du pilote
  chipset, de la Game Bar et du plan Équilibré. (Sources : rapports XDA/Phoronix/MSI ; pas de
  document AMD officiel trouvé — niveau de confiance « Probable ».)
- **Windows 11 26H1** est réservé aux nouveaux appareils (build 28000) et n'est pas proposé en mise à
  jour de 24H2/25H2 : la détection doit se baser sur le numéro de build et non sur une liste figée.
- **Optimisations pour les jeux fenêtrés** (DX10/DX11 fenêtré et sans bordure) : fonctionnalité
  documentée par Microsoft ; activée automatiquement si Auto HDR est activé.
- **SystemResponsiveness = 0**, réglage très répandu, est **ramené à 20** (la valeur par défaut) selon
  la documentation Microsoft MMCSS : c'est un placebo.
- **Win32PrioritySeparation = 0x26** (38), réglage très répandu, est **équivalent à la valeur par défaut
  (2)** d'un Windows client (intervalles courts, variables, boost 3:1 du premier plan) : placebo.
- **useplatformclock** (HPET forcé) : la documentation Microsoft QPC indique qu'une source de temps
  plateforme impose un appel système et environ 0,8 à 1,0 µs par lecture de QPC, contre quelques
  dizaines de cycles avec le TSC : FrameCore ne force jamais HPET et propose au contraire de retirer
  ce forçage s'il est détecté.
- **timeBeginPeriod** : depuis Windows 10 2004 la résolution du timer n'est plus globale, et depuis
  Windows 11 elle n'est plus garantie pour une fenêtre masquée/minimisée.
  `GlobalTimerResolutionRequests` restaure l'ancien comportement global (expérimental, coût
  énergétique).

## Sources principales

- WinUtil : `https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/config/tweaks.json`
- PresentMon : `https://github.com/GameTechDev/PresentMon` (README-ConsoleApplication.md, v2.4.x)
- LibreHardwareMonitor : `https://github.com/LibreHardwareMonitor/LibreHardwareMonitor`, paquet NuGet 0.9.6
- ADLX : `https://gpuopen.com/adlx/` et en-têtes `GPUOpen-LibrariesAndSDKs/ADLX/SDK/Include`
- NVML : en-tête `nvml.h` (NVIDIA/go-nvml), documentation `docs.nvidia.com/deploy/nvml-api`
- Microsoft : MMCSS, timeBeginPeriod, *Acquiring high-resolution time stamps*, *Windows 11 release information*,
  *Options to optimize gaming performance in Windows 11*, *Optimizations for windowed games in Windows 11*
- Intel XTU : page de téléchargement 17881 (branches 7.14 et 10.x)
