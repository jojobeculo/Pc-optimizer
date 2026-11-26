# FrameCore

**Centre de contrôle des performances Windows 11 pour le jeu — gratuit, local, réversible.**

FrameCore analyse votre PC (processeur, carte graphique, mémoire, carte mère, stockage, écrans, réseau, Windows),
explique ce qui limite ses performances et propose des optimisations adaptées **à votre machine**, avec pour chacune
son mécanisme, son impact attendu, son niveau de preuve, ses risques et sa méthode de restauration. Chaque
modification est enregistrée avec la valeur exacte d'origine et peut être annulée individuellement ou en bloc.

> **État du projet — version 2.0.** Le moteur (catalogue, transactions, vérification, statistiques, estimation) est couvert
> par des tests automatisés, et l'application est testée sur un vrai Windows (machine virtuelle jetable de CI) : chaque
> optimisation y est appliquée puis restaurée à l'identique, chaque écran est visité et capturé, et les parcours
> utilisateur (benchmark, optimisation mesurée, restauration) y sont exécutés. Les fonctions qui exigent un matériel absent
> de cette VM (overclocking NVIDIA/AMD, capteurs de température, capture d'un vrai jeu) n'ont pas pu être testées :
> voir [docs/FONCTIONNALITES.md](docs/FONCTIONNALITES.md) et [docs/RAPPORT-TESTS.md](docs/RAPPORT-TESTS.md).

## Fonctionnalités

| Domaine | Ce que fait FrameCore |
|---|---|
| Tableau de bord | Diagnostic hiérarchisé, jauge factuelle des réglages actifs, actions rapides, alerte d'opération interrompue |
| Optimisation | 3 niveaux (Équilibré, Performances, Performance maximale), plan propre au PC avant application, mesure avant/après, progression réelle par étapes, vérification et réparation automatiques |
| Jeux & FPS | 91 jeux : FPS estimés avant/après avec plage, limitation, confiance et origine du chiffre ; 3 jeux mis en avant ; explorateur ; calibration par vos mesures réelles |
| Benchmark | Benchmark rapide (≈ 5 min ou 1 min 30), comparaison statistique, laboratoire (CPU, RAM, GPU/VRAM, stockage, réactivité, calcul IA, réseau, stress combiné), capture en jeu (ETW, PresentMon, CSV) |
| Matériel | Détection complète avec **fiabilité de chaque donnée**, mesures en direct, procédure guidée XMP/EXPO |
| Pilotes & BIOS | Inventaire, signataires, Windows Update, sources officielles, historique des versions ; BIOS : informations et liens officiels, jamais de flash |
| Overclocking | GPU NVIDIA (NVML) et AMD (ADLX) : manuel, automatique progressif, test de stabilité, réglages d'usine ; CPU : test et conseils |
| Restauration | Valeur exacte d'origine pour chaque modification, restauration élément/catégorie/profil/tout, rapports, reprise, vérification après redémarrage |
| Mode Pro | Chaque réglage avec sa fiche complète, démarrage et services, profils exportables, surveillance, réglages écartés |
| Catalogue | 73 optimisations documentées ([docs/CATALOGUE.md](docs/CATALOGUE.md)) + 19 « astuces » écartées avec la raison |

## Ce que FrameCore ne fera jamais

Désactiver Microsoft Defender, le pare-feu, Windows Update, Secure Boot, le TPM, VBS/HVCI ou les mitigations
processeur ; supprimer des composants Windows ; « nettoyer » la RAM en vidant la liste d'attente ; tuer des processus
au hasard ; supprimer le fichier d'échange ; défragmenter un SSD ; forcer HPET ; installer un pilote non signé ;
flasher un BIOS ; télécharger depuis un site tiers ; envoyer vos données sans consentement explicite.

## Installation

1. Téléchargez `FrameCore-Setup-<version>-x64.exe` et vérifiez son empreinte SHA-256 (`SHA256SUMS.txt`).
2. Lancez l'installateur (droits administrateur requis pour écrire dans *Program Files*).
3. L'exécutable n'est **pas signé numériquement** : Windows SmartScreen peut afficher « Éditeur inconnu ».
   Cliquez sur *Informations complémentaires* → *Exécuter quand même* si vous faites confiance à la source.

Installation silencieuse : `FrameCore-Setup.exe /S` — désinstallation silencieuse :
`"%ProgramFiles%\FrameCore\Uninstall.exe" /S` (ajoutez `/PURGE` pour supprimer aussi les données).

Configuration requise : Windows 11 x64 (Windows 10 1809+ accepté avec un avertissement), 250 Mo d'espace disque.

## Compiler

```bash
# Linux / WSL (nécessite .NET SDK 10 et NSIS 3)
build/build.sh
# Windows
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Sorties : `publish/FrameCore/` (application autonome) et `artifacts/FrameCore-Setup-<version>-x64.exe`.

Tests seuls : `dotnet test tests/FrameCore.Tests/FrameCore.Tests.csproj`.

## Documentation

- [Guide d'utilisation](docs/GUIDE-UTILISATION.md) · [Guide de restauration](docs/GUIDE-RESTAURATION.md) · [Installation](docs/INSTALLATION.md)
- [Catalogue des optimisations](docs/CATALOGUE.md) · [Benchmarks et estimation des FPS](docs/BENCHMARKS.md) · [Overclocking](docs/OVERCLOCKING.md)
- [Architecture](docs/ARCHITECTURE.md) · [Système de design](docs/DESIGN.md)
- [Rapport d'audit](docs/AUDIT.md) · [Rapport de tests](docs/RAPPORT-TESTS.md) · [État des fonctionnalités](docs/FONCTIONNALITES.md) · [Idées étudiées](docs/IDEES.md)
- [Dépendances et licences](docs/DEPENDANCES-ET-LICENCES.md) · [Notices tierces](docs/THIRD_PARTY_NOTICES.md)
- Recherches : [logiciels existants](docs/research/01-ETUDE-LOGICIELS-EXISTANTS.md),
  [technologies et API](docs/research/02-ETUDE-TECHNOLOGIES-ET-API.md),
  [inventaire des optimisations](docs/research/03-INVENTAIRE-OPTIMISATIONS.md)

## Confidentialité

Aucune télémétrie. Les données (profil matériel, journal, sauvegardes, benchmarks, réglages) restent dans
`%LOCALAPPDATA%\FrameCore` sur la machine. La vérification des mises à jour est désactivée par défaut.

## Licence

[MIT](LICENSE). Les composants tiers conservent leur licence (voir [THIRD_PARTY_NOTICES](docs/THIRD_PARTY_NOTICES.md)).
