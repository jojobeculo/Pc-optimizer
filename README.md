# FrameCore

**Centre de contrôle des performances Windows 11 pour le jeu — gratuit, local, réversible.**

FrameCore analyse votre PC (processeur, carte graphique, mémoire, carte mère, stockage, écrans, réseau, Windows),
explique ce qui limite ses performances et propose des optimisations adaptées **à votre machine**, avec pour chacune
son mécanisme, son impact attendu, son niveau de preuve, ses risques et sa méthode de restauration. Chaque
modification est enregistrée avec la valeur exacte d'origine et peut être annulée individuellement ou en bloc.

> ⚠️ **État du projet** : version 1.x. La logique (catalogue, moteur transactionnel, statistiques, analyse) est
> couverte par des tests automatisés. Les fonctions qui dépendent d'un matériel précis (overclocking NVIDIA/AMD,
> capteurs, capture ETW) ont été écrites d'après la documentation officielle mais **n'ont pas pu être testées sur
> du matériel réel** dans l'environnement de développement. Voir [docs/RAPPORT-TESTS.md](docs/RAPPORT-TESTS.md).

## Fonctionnalités

| Domaine | Ce que fait FrameCore |
|---|---|
| Diagnostic | Détection complète du matériel et de Windows, constats hiérarchisés (critique / avertissement / suggestion) |
| Optimisation simple | Profils prêts à l'emploi ; plan détaillé affiché **avant** application ; exclusion automatique des réglages incompatibles |
| Mode Pro | Chaque réglage individuellement : valeur actuelle, valeur proposée, mécanisme, impact, confiance, risque, compatibilité, redémarrage, appliquer / restaurer |
| Démarrage & services | Inventaire type Autoruns, classification (indispensable → à risque), désactivation réversible |
| Pilotes | Inventaire, signataires, mises à jour Windows Update, liens officiels des fabricants uniquement |
| BIOS / firmware | Version, date, Secure Boot, ReBAR, TPM ; liens officiels ; **jamais de flash automatique** |
| Overclocking GPU | NVIDIA (NVML) et AMD (ADLX) : offsets, limite de puissance, préréglages AMD, réglage automatique progressif avec test de stabilité et retour aux réglages d'usine |
| CPU | Surveillance, test de stabilité, conseils PBO / Curve Optimizer / limites Intel (réglages à faire dans le BIOS ou les outils constructeur) |
| Benchmark | Capture des frametimes en jeu (ETW ou PresentMon), 1 % / 0,1 % low, sessions répétées, comparaison statistique avant/après |
| Sauvegarde | Journal transactionnel, restauration élément / catégorie / profil / tout, export/import, point de restauration Windows |
| Rapport | Export HTML/JSON anonymisable, rien n'est envoyé automatiquement |

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

Configuration requise : Windows 11 x64 (Windows 10 1809+ accepté avec un avertissement), 200 Mo d'espace disque.

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

- [Guide d'utilisation](docs/GUIDE-UTILISATION.md)
- [Guide de restauration](docs/GUIDE-RESTAURATION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Dépendances et licences](docs/DEPENDANCES-ET-LICENCES.md) · [Notices tierces](docs/THIRD_PARTY_NOTICES.md)
- Recherches : [logiciels existants](docs/research/01-ETUDE-LOGICIELS-EXISTANTS.md),
  [technologies et API](docs/research/02-ETUDE-TECHNOLOGIES-ET-API.md),
  [inventaire des optimisations](docs/research/03-INVENTAIRE-OPTIMISATIONS.md)

## Confidentialité

Aucune télémétrie. Les données (profil matériel, journal, sauvegardes, benchmarks, réglages) restent dans
`%LOCALAPPDATA%\FrameCore` sur la machine. La vérification des mises à jour est désactivée par défaut.

## Licence

[MIT](LICENSE). Les composants tiers conservent leur licence (voir [THIRD_PARTY_NOTICES](docs/THIRD_PARTY_NOTICES.md)).
