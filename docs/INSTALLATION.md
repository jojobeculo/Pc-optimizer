# Installation et portabilité

## Installer

1. Récupérez `FrameCore-Setup-<version>-x64.exe` et, si possible, vérifiez son empreinte avec `SHA256SUMS.txt`
   (PowerShell : `Get-FileHash .\FrameCore-Setup-2.0.0-x64.exe`).
2. Lancez-le. Windows demande les droits administrateur pour écrire dans *Program Files*.
3. SmartScreen peut afficher « Éditeur inconnu » : l'exécutable n'est pas signé numériquement. *Informations complémentaires*
   → *Exécuter quand même* si la source est de confiance.
4. Choisissez la langue, les raccourcis (menu Démarrer, Bureau) et le dossier.

Prérequis vérifiés par l'installateur : Windows 64 bits, Windows 10 1809 ou plus récent (avertissement si ce n'est pas
Windows 11). Aucun runtime à installer : .NET est embarqué.

| Commande | Effet |
|---|---|
| `FrameCore-Setup.exe /S` | installation silencieuse |
| `FrameCore-Setup.exe /S /D=C:\Apps\FrameCore` | dossier personnalisé (dernier argument) |
| `"%ProgramFiles%\FrameCore\Uninstall.exe" /S` | désinstallation silencieuse, données conservées |
| `… Uninstall.exe /S /PURGE` | désinstallation et suppression des données de l'utilisateur |

## Mise à jour

Relancez simplement le nouvel installateur : les fichiers de l'application sont remplacés, vos données restent.
La vérification automatique des mises à jour (Paramètres) est désactivée par défaut ; elle ne contacte que l'adresse que
vous configurez et n'installe un fichier téléchargé qu'après vérification de son empreinte SHA-256 (et de sa signature
quand elle existe), avec votre accord.

## Partager avec un autre PC

| Contenu de l'installateur (commun) | Données propres à chaque PC (jamais dans l'installateur) |
|---|---|
| application, polices, icônes, catalogue d'optimisations, bibliothèque de jeux, tables matériel, langues, documentation, réglages par défaut | profil matériel, journal et sauvegardes, rapports de vérification, benchmarks, mesures en jeu, historique des pilotes, profils d'overclocking, préférences |
| `C:\Program Files\FrameCore` | `%LOCALAPPDATA%\FrameCore` |

Copier l'installateur sur le PC d'un ami suffit : FrameCore y fera sa propre détection. Le script de build vérifie
qu'aucun fichier de données personnelles ne se trouve dans le dossier publié.

## Désinstaller

*Paramètres Windows → Applications → FrameCore → Désinstaller*, ou le raccourci de désinstallation. Restaurez d'abord vos
optimisations (page Restauration → Tout restaurer) si vous souhaitez aussi supprimer vos données.
