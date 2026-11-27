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

## Processeurs

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
