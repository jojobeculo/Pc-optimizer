# Overclocking, réglage matériel et surveillance

FrameCore distingue trois niveaux, toujours indiqués dans l'interface :

| Niveau | Ce que fait FrameCore |
|---|---|
| **Surveillance** | Lecture des fréquences, températures, utilisation, puissance (quand le pilote ou un capteur les expose) |
| **Réglage pris en charge** | Modification via l'interface officielle du constructeur, avec test de stabilité et retour aux valeurs d'usine |
| **Recommandation BIOS / outil constructeur** | Explications et procédure guidée ; FrameCore ne modifie rien lui-même |

## Cartes graphiques

| | NVIDIA (NVML, fourni avec le pilote) | AMD (ADLX, fourni avec le pilote) | Intel Arc |
|---|---|---|---|
| Surveillance | fréquences, température, puissance, VRAM, lien PCIe, BAR1 | via ADLX et capteurs | utilisation (compteurs Windows) |
| Offset fréquence cœur / mémoire | oui (`nvmlDeviceSetClockOffsets`, repli `GpcClkVfOffset`) | réglage manuel de puissance ; préréglages du pilote | non (pas d'API publique) |
| Limite de puissance | oui, dans la plage autorisée par le pilote | oui (réglage de puissance manuel) | non |
| Tension | **jamais exposée** | **jamais exposée** (l'undervolt passe par le préréglage officiel AMD) | — |
| Ventilation | non (laissée au pilote / logiciel constructeur) | non | non |
| Automatique | montée par paliers de 15 MHz, test de stabilité D3D11 à chaque palier, recul au dernier palier stable, contrôle de la bande passante mémoire, validation finale, retour à l'origine si échec | préréglages AMD (overclock GPU, overclock VRAM, undervolt) + validation de 3 min | — |

Les offsets NVML ne survivent pas à un redémarrage. Les réglages ADLX sont conservés par le pilote AMD, qui revient
lui-même aux valeurs par défaut après un plantage.

### Niveaux du réglage automatique (page Overclocking)

| Niveau | NVIDIA (fréquences via NVML, aucune tension) | AMD (réglage automatique du pilote, ADLX) |
|---|---|---|
| **Léger** | cœur jusqu'à +75 MHz, mémoire jusqu'à +200 MHz, paliers de 45 s, arrêt à 80 °C, **2 paliers** de marge, limite de puissance inchangée, validation finale 3 min | sous-voltage automatique d'AMD |
| **Modéré** | +150 / +500 MHz, paliers de 60 s, 83 °C, 1 palier de marge, limite de puissance au maximum autorisé par le pilote, validation 4 min | overclocking automatique du GPU par AMD |
| **Élevé** | +250 / +1000 MHz, paliers de 90 s, 85 °C, limite de puissance au maximum, validation 6 min | sous-voltage automatique **+** limite de puissance au maximum (moins de tension pour la même fréquence = plus de marge de boost) |
| **Personnalisé** | plafond, durée des paliers et mémoire choisis à la main | un réglage automatique précis d'AMD |

Les plafonds restent bornés par les plages que le pilote annonce. En cas d'échec de la validation finale, tout revient
aux valeurs d'origine (limite de puissance comprise).

**Pourquoi pas de tension sur NVIDIA** : l'interface officielle (NVML) ne permet pas de la régler ; les outils qui le
font utilisent des fonctions non documentées du pilote, que FrameCore n'intègre pas. Sur les cartes récentes, monter la
tension n'apporte presque rien (la carte est limitée par sa puissance) : le gain vient des décalages de fréquence et de
la limite de puissance. Sur AMD, la tension est modifiée **par le pilote lui-même** (sous-voltage automatique), au
niveau Léger et Élevé.

## Processeurs

### Guide pas à pas par niveau (page Overclocking → « Overclocking du processeur, pas à pas »)

FrameCore reconnaît la famille du processeur (Ryzen AM5, Ryzen X3D AM5, Ryzen AM4, Intel Core 12e–14e « K », Core
Ultra 200S « K », Intel verrouillé ; rien pour un portable, une VM ou un processeur inconnu) et propose trois niveaux
avec des **valeurs concrètes** à saisir dans le BIOS, par exemple pour un Ryzen 5 7600 :

| Niveau | Réglages |
|---|---|
| Léger | EXPO I (DDR5-6000 CL30) ; Curve Optimizer tous cœurs −10 |
| Modéré | + PBO Advanced, limites « Motherboard », Curve Optimizer −20, limite thermique 85 °C, FCLK 2000 MHz, UCLK = MEMCLK |
| Élevé | + Max CPU Boost Clock Override +200 MHz, Curve Optimizer par cœur −30 (−20 sur les 2 meilleurs cœurs), 90 °C |

Pour chaque réglage : à quoi il sert, son chemin dans le BIOS de la marque choisie (**ASUS, MSI, Gigabyte/AORUS,
ASRock** — détectée automatiquement, modifiable dans la liste) et une **illustration schématique de l'écran du BIOS**
avec l'option mise en évidence. Ces illustrations sont dessinées par FrameCore (couleurs et disposition typiques, aucun
logo, pas de captures officielles) ; les noms et emplacements peuvent varier selon la carte et la version du BIOS.
Côté Windows : comment vérifier qu'EXPO/XMP est actif (Gestionnaire des tâches → Performances → Mémoire → Vitesse) et
l'alternative AMD Ryzen Master pour essayer Curve Optimizer avant de reporter les valeurs dans le BIOS.

Section **Réglages très avancés** : VSOC (≤ 1,30 V, limite d'AMD), VDD/VDDQ, Curve Optimizer par cœur, Curve Shaper,
Memory Context Restore, PPT/TDC/EDC ; Intel : microcode 0x12B+, jamais plus de 1,40 V sous charge, AC/DC Load Line,
IA CEP, ring. Aucune valeur ne monte une tension de cœur ; FrameCore n'écrit jamais dans le BIOS.

Données : `catalog/knowledge/cpu-oc-guide.json` (vérifiées par des tests : 3 niveaux par famille, chaque réglage
localisé dans le BIOS des 4 marques, textes FR/EN, aucune tension dangereuse).


Aucune interface publique et sûre ne permet de modifier PBO, Curve Optimizer, les limites de puissance Intel ou les
ratios depuis une application tierce sans pilote noyau. FrameCore :

- détecte le modèle, le déblocage du multiplicateur, le 3D V-Cache (X3D mono/bi-CCD), les cœurs P/E ;
- surveille fréquence effective, utilisation par cœur, « % Performance Limit » et, avec PawnIO installé par l'utilisateur,
  température et puissance (LibreHardwareMonitor) ;
- propose un test de stabilité (calculs vérifiés + erreurs matérielles WHEA) ;
- donne des recommandations adaptées (PBO / Curve Optimizer pour Ryzen, limites PL1/PL2 « Intel Default » et microcode
  pour les 13e/14e générations) et ouvre les outils officiels (AMD Ryzen Master, Intel XTU) s'ils sont installés ;
- affiche une **procédure pas à pas** propre au processeur détecté : BIOS à jour et Benchmark complet de référence,
  réglage par petits paliers (Curve Optimizer −10, −15, −20… pour Ryzen ; ratio +1 sur un Intel débloqué avec chipset Z),
  test de stabilité et Benchmark complet à chaque palier, retour au palier précédent au premier problème, et le moyen de
  tout annuler (« Load Optimized Defaults » / effacement du CMOS). Aucune tension n'est jamais modifiée ; aucune procédure
  n'est affichée quand le processeur ne s'y prête pas ou n'est pas identifié avec certitude.

## Mémoire : XMP / EXPO

FrameCore compare la vitesse configurée (SMBIOS) à la vitesse nominale du kit (décodage de la référence) et signale un
profil **probablement** inactif. L'activation se fait uniquement dans le BIOS : une procédure guidée adaptée au fabricant
de la carte mère (ASUS, MSI, Gigabyte, ASRock, PC de marque) est affichée dans Matériel → Mémoire vive. Sur une machine
virtuelle, aucune déduction n'est faite.

## Sécurité

Aucun accès BIOS non documenté, aucune écriture dans des registres matériels, aucun pilote noyau embarqué, aucune
tension modifiée. Le stress combiné CPU + GPU s'arrête au-delà de 95 °C (CPU) ou 90 °C (GPU).

> Ces fonctions n'ont pas pu être testées sur une vraie carte NVIDIA ou AMD dans l'environnement de développement
> (machine virtuelle sans GPU) : elles sont signalées comme à tester progressivement.
