# Vérification des exigences — FrameCore 2.0.0

Contrôle point par point des deux cahiers des charges, dans le dépôt (code, câblage dans l'interface, tests) et sur un
vrai Windows (machine virtuelle jetable de CI, en administrateur **et** en utilisateur standard).

Statuts : **Fait** (implémenté, câblé dans l'interface et vérifié) · **Partiel** (une partie manque ou n'est vérifiable
qu'en partie, détail donné) · **Non testable ici** (implémenté, mais le matériel nécessaire est absent de
l'environnement de test : jamais simulé ni annoncé comme testé) · **Non fait** (absent, raison donnée).

Preuves : « UT » = test unitaire (`tests/FrameCore.Tests`, 131 tests) ; « IT » = test d'intégration sur Windows réel
(`tests/FrameCore.WindowsIntegrationTests`) ; « Visite » = visite automatique de l'interface sur Windows avec captures
(`--ui-tour`) ; « Parcours » = action utilisateur réellement exécutée pendant la visite sur la machine jetable.

## 1. Architecture et règles générales

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| C# / .NET 10 LTS, WPF + MVVM, Windows 11 x64 | Fait | `FrameCore.slnx`, 15 projets ; compilation Release sans avertissement |
| 15 modules séparés (Core, détection, Windows, démarrage, CPU, GPU, RAM, stockage, réseau, écran, pilotes, BIOS, benchmark, sauvegarde, UI) | Fait | `src/` ; chaque module est utilisé par l'interface (contrôle des références) |
| Gratuit, local, aucune télémétrie, aucun envoi sans consentement | Fait | un seul client HTTP (mise à jour, désactivée par défaut, adresse choisie par l'utilisateur) ; test de latence Internet derrière une case de consentement |
| Diagnostics sans droits administrateur, UAC seulement quand nécessaire, regroupé | Fait | manifeste `asInvoker` ; processus élevé lancé une fois par lot ; visite en utilisateur standard ; aller-retour réel via le processus élevé (33 réglages, état d'origine retrouvé) |
| Liste blanche d'opérations, aucune commande PowerShell construite depuis l'interface | Fait | outils autorisés : `bcdedit`, `defrag`, `powercfg`, `netsh`, `dism`, `mdsched`, `cleanmgr` (énumération fermée) ; aucun appel PowerShell ; le processus élevé n'accepte que des actions identiques au catalogue (UT) |
| Interdits : Defender, pare-feu, Windows Update, Secure Boot/TPM, VBS/HVCI, composants critiques, services au hasard, pilotes non signés, registres matériels, tensions, OC non supporté, BIOS automatique ou tiers, packs de pilotes, pagefile supprimé, défragmentation SSD, HPET forcé, faux nettoyeur RAM, processus tués au hasard, anti-cheat/audio/périphériques désactivés | Fait | `SafetyPolicy` + UT de catalogue (aucune cible protégée) ; anti-cheats protégés dans la base de démarrage (UT) ; pagefile seulement *réparé* s'il est désactivé ; HPET seulement *retiré* s'il est forcé ; aucune purge de mémoire |
| Aucune optimisation sur le PC de développement sans accord | Fait | les tests qui modifient Windows ne tournent qu'avec `FRAMECORE_DISPOSABLE_VM=1` sur une VM détruite après usage |
| Aucune signature revendiquée sans certificat | Fait | documenté (README, INSTALLATION) : exécutable non signé, avertissement SmartScreen possible |
| Français par défaut + anglais | Fait | 1 471 clés, identiques dans les deux langues (UT) ; changement de langue sans relancer l'analyse |

## 2. Détection matérielle et fiabilité des données

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| CPU, GPU, RAM (barrettes, canaux, XMP/EXPO), carte mère, BIOS, stockage (SSD/HDD, SMART), écrans (EDID, VRR, HDR, fréquence), réseau, Windows, sécurité | Fait / Partiel | IT + visite ; sur VM : SPD, SMART, VRR non observables (affichés « indéterminé », jamais inventés) |
| Niveau de confiance de chaque donnée (mesuré / déclaré / déduit / indisponible) | Fait | onglet « Fiabilité des données » (visite), `DataQualityAssessor` (UT) |
| Machines virtuelles reconnues (pas de « double canal », pas de conseils de BIOS/chipset) | Fait | constaté puis corrigé sur la VM |
| Capteurs de température / puissance | Non testable ici | via LibreHardwareMonitor uniquement si le pilote signé PawnIO est déjà installé ; la VM n'expose aucun capteur |

## 3. Catalogue et profils

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Catalogue documenté (id, nom, description, catégorie, objectif, compatibilité, valeur initiale, valeur proposée, impact, risque, confiance, méthode d'application / vérification / restauration, persistance, dépendances, conflits) | Fait | 73 fiches (`catalog/tweaks`), validation automatique (UT), `docs/CATALOGUE.md` généré |
| Niveaux de confiance (confirmé 30, probable 28, expérimental 13, non recommandé 2) | Fait | UT ; les expérimentaux ne sont jamais cochés d'office |
| Mythes rejetés avec la raison | Fait | 19 « astuces » écartées (`rejected-tweaks.json`), visibles en mode Pro |
| 3 niveaux Équilibré / Performances / Performance maximale | Fait | Parcours : profil Équilibré appliqué, vérifié, restauré ; plans des 3 niveaux capturés |
| Plan propre au PC avant application, conflits exclus | Fait | `PlanBuilder` (UT) ; un plan ne contient jamais deux réglages en conflit |

## 4. Mode Simple et Mode Pro

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Mode simple : choix du niveau, plan, application, progression réelle, résultat vérifié, mesure avant/après | Fait | Parcours ; défilement automatique vers la progression et le résultat |
| Mode Pro : chaque réglage avec sa fiche, appliquer / restaurer individuellement | Fait | Parcours : un réglage appliqué avec son bouton, puis sa catégorie restaurée (0 modification active) |
| Profils personnalisés exportables / importables | Fait | mode Pro › Profils (visite) |
| Surveillance temps réel | Fait | mode Pro › Surveillance, cartes de l'accueil (CPU, RAM, GPU) |

## 5. Services, démarrage, RAM

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Inventaire du démarrage (Run, dossier, tâches, services, extensions), classification, désactivation réversible sans suppression | Fait | Parcours : entrée réelle désactivée puis réactivée ; UT (StartupApproved, anti-cheats protégés) |
| Mises à jour de sécurité conservées, jamais proposées d'office | Fait | navigateurs, Acrobat, Java « à conserver » (UT) |
| Analyse du temps de démarrage de Windows (durées mesurées par Windows, tendance, applications / pilotes / services qui ralentissent) | Partiel | journal Diagnostics-Performance ; UT (format des événements, médiane, tendance, classement) ; sur la VM Windows Server ce journal n'existe pas : message « aucune donnée » vérifié en administrateur et en utilisateur standard ; lecture d'un vrai historique non vérifiable ici |
| Services tiers : passage Manuel/Automatique réversible | Fait | mode Pro › Services |
| Module RAM sans faux « nettoyeur » | Fait | analyse (canaux, XMP, barrettes mixtes, quantité), processus gourmands, diagnostic mémoire Windows ; aucune purge de la liste d'attente |

## 6. Overclocking, BIOS, pilotes

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| GPU NVIDIA (NVML) et AMD (ADLX) : manuel, automatique progressif, test de stabilité, retour au dernier réglage stable, réglages d'usine | Non testable ici — **à considérer comme expérimental** | algorithme testé (UT : recul après échec, plafond, retour à l'origine) ; aucune carte réelle disponible |
| GPU Intel Arc | Non fait | aucune API publique de réglage |
| CPU AMD/Intel : conseils (PBO, Curve Optimizer, limites de puissance) + test de stabilité vérifié + contrôle WHEA | Fait (conseils, test) | aucune écriture dans le processeur (pas d'API publique sans pilote noyau) ; test exécuté sur la VM |
| XMP/EXPO : détection + procédure guidée par fabricant de carte mère | Fait | jamais modifié depuis Windows (réglage du BIOS) |
| BIOS : version, date, liens officiels du fabricant, jamais de flash | Fait | page Matériel › Carte mère |
| Pilotes : inventaire, signataires, Windows Update (avec validation), sources officielles, historique des versions, retour à un pilote précédent (Gestionnaire de périphériques) | Fait / Partiel | inventaire réel ; installation via Windows Update non déclenchée sur la VM |
| Carte graphique par application (réglage officiel de Windows) | Fait | ajouté : mode Pro › Carte graphique ; UT + Parcours (réglage puis retour au choix de Windows) |

## 7. Goulots d'étranglement (bottleneck)

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Détection à partir de mesures réelles (charge GPU/CPU, VRAM, plafond d'images) | Fait / Non testable ici | `BottleneckAnalyzer` (UT : limitation GPU, CPU, VRAM saturée, FPS plafonnés) ; capture d'un vrai jeu impossible sur la VM |
| Limitation par jeu (CPU / GPU / plafond) dans l'estimateur | Fait | page Jeux & FPS |

## 8. Benchmark rapide, laboratoire, inférence IA, stress combiné

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Benchmark rapide ≈ 5 min (express ≈ 1 min 30), composants séparés, médianes, IC 95 %, progression réelle | Fait | Parcours (express) ; UT statistiques et progression |
| Comparaison avant/après avec seuil de bruit et confiance ; changement de pilote/Windows signalé | Fait | UT ; Parcours |
| Comparaison partageable (HTML, sans données personnelles) | Fait | ajouté ; Parcours (export réel) |
| Laboratoire : CPU, RAM, stockage, réactivité, calcul type IA (GFLOPS FP32) | Fait | Parcours (réactivité, calcul type IA) ; onglet affiché et vérifié |
| Laboratoire : GPU / VRAM | Non testable ici | pas de GPU matériel sur la VM |
| Réseau : latence / gigue / pertes vers la box ; vers Internet seulement avec consentement | Fait | case de consentement obligatoire |
| Débit Internet (téléchargement) | Non fait | aucun serveur de test officiel et libre de droits n'est embarqué ; seule la latence est mesurée |
| Inférence IA réelle (ONNX Runtime, llama.cpp) | Non fait | dépendances et modèles volumineux non embarqués ; remplacée par un indicateur de calcul clairement nommé |
| Stress combiné CPU + GPU avec arrêt thermique (95 °C CPU / 90 °C GPU), limité à 2 min sans capteurs | Non testable ici | pas de GPU ni de capteurs sur la VM |
| Outils tiers non embarqués sans droits de redistribution | Fait | PresentMon, DiskSpd, OCCT… seulement cités avec lien officiel ; licences inventoriées |

## 9. Estimateur de FPS (50 à 100 jeux)

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| 91 jeux, résolution et préréglage, plage d'incertitude, confiance, origine du chiffre, limitation | Fait | UT (13) ; rendu vérifié avec une machine de référence déclarée (91/91 estimés) |
| 3 jeux mis en avant après optimisation (avant → après) | Fait | le gain ne vient que d'un benchmark avant/après confirmé et correspondant à l'état actuel |
| « Explorer les jeux » : recherche, genres, tri, détail | Fait | visite |
| Calibration par captures réelles (PresentMon / ETW) | Fait / Non testable ici | logique testée ; capture d'un vrai jeu impossible sur la VM |
| GTA VI seulement si vérifié | Fait | exclu (aucune version PC confirmée), expliqué dans la page |
| Matériel non identifié → aucune estimation inventée | Fait | VM : bandeau « carte graphique non identifiée » |

## 10. Auto Verify & Repair

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Relecture après application, 2 réécritures maximum puis restauration | Fait | UT ; Parcours (9/9 vérifiées) |
| Régression de performance mesurée → restauration des seules modifications concernées, nouvelle mesure | Fait / Partiel | UT ; sur la VM la dégradation mesurée (stockage) est correctement attribuée à un facteur externe, sans restauration inutile |
| « Vérifier et réparer maintenant » (après une mise à jour Windows) | Fait | Parcours |

## 11. Sauvegarde, restauration, reprise après redémarrage

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Journal transactionnel écrit avant modification, rollback de l'action en échec | Fait | UT |
| Restauration exacte (élément, catégorie, profil, tout) | Fait | IT : 53 réglages appliqués puis restaurés à l'identique ; Parcours : 0 modification active après « Tout restaurer » |
| Réglage déjà en place : rien n'est écrit ni journalisé, pas de redémarrage demandé | Fait | Parcours (14 « déjà en place » détectés) |
| Reprise d'une optimisation interrompue sans double application | Fait / Partiel | UT ; coupure de courant réelle non simulée |
| Vérification après redémarrage | Fait / Partiel | logique testée ; redémarrage réel non effectué sur la VM |
| Point de restauration Windows | Non testable ici | indisponible sur Windows Server (message clair) ; appelé sur Windows 11 |
| Export / import des sauvegardes | Fait | page Restauration |

## 12. Fonctionnalités additionnelles et idées

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Au moins 10 idées analysées (intérêt, faisabilité, risques, priorité) | Fait | `docs/IDEES.md` : 12 idées demandées + 12 supplémentaires |
| Idées implémentées | Fait | mesure avant/après intégrée, fiabilité des données, réglages écartés, reprise, calibration, vérification après redémarrage, comparaison partageable, carte graphique par application, analyse du temps de démarrage, rapport de santé, détection de bridage, détection de régression après mise à jour, centre de restauration |
| Idées restantes | Non fait (proposé) | mode « session de jeu », historique continu des capteurs, vérification d'écran par jeu |

## 13. Design, accessibilité, performance de l'application

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Système de design (jetons, typographie, espacements, composants), thèmes sombre et clair | Fait | `docs/DESIGN.md` ; contrastes WCAG AA calculés |
| Polices OFL et icônes ISC embarquées avec licences | Fait | `THIRD_PARTY_NOTICES.md` |
| Animations utiles, désactivées si « Effets d'animation » est coupé dans Windows | Fait | `Motion.Enabled` |
| Accessibilité : noms accessibles, cibles ≥ 22 px, focus visible, clavier | Fait | audit automatique : 0 problème ; parcours clavier de chaque page |
| Mise à l'échelle DPI et redimensionnement | Fait | manifeste PerMonitorV2 ; rendu vérifié à 1366×768 / 125 % et 1920×1080 / 150 % : aucun contenu coupé ; petite fenêtre ; fenêtre jamais plus grande que l'écran |
| États vides, chargement, erreurs | Fait | messages dédiés (aucun benchmark, aucune estimation possible, rien à restaurer, calcul en cours) ; erreurs inattendues journalisées avec leur cause, sans avalanche de fenêtres |
| Penpot / Figma | Non fait | aucun serveur connecté dans l'environnement ; documenté |
| Performance de l'application | Fait | fenêtre en ≈ 1 s, diagnostic 3 à 6 s, ≈ 180 à 230 Mo, ≈ 2 % d'un cœur au repos (mesures réelles) |

## 14. Séparation des données et portabilité

| Exigence | Statut | Où / comment c'est vérifié |
|---|---|---|
| Application (Program Files) séparée des données de chaque PC (`%LOCALAPPDATA%\FrameCore`) | Fait | `AppPaths` ; désinstallation : application supprimée, données conservées (ou `/PURGE`) |
| Installateur sans données personnelles ni profil matériel du développeur | Fait | le script de build refuse de produire l'installateur si le dossier publié contient réglages, journaux, sauvegardes, benchmarks, rapports, profils, journaux d'exécution, un chemin de profil utilisateur ou un chemin de la machine de build (build déterministe, symboles intégrés, aucun .pdb) ; aucun profil matériel n'est créé avant le premier lancement sur le PC de l'utilisateur |
| Installateur x64 autonome (runtime .NET embarqué), raccourcis, entrée « Applications », désinstallation | Fait | test d'installation silencieuse / lancement / désinstallation sur Windows |
| Mise à jour : manifeste HTTPS, empreinte SHA-256 obligatoire, accord de l'utilisateur | Fait / Non testable ici | aucun serveur de mise à jour configuré (désactivée par défaut) |

## 15. Livrables

| Livrable | Statut |
|---|---|
| Code source, catalogue, bibliothèque de jeux, tables matériel | Fait (dépôt) |
| Installateur `FrameCore-Setup-2.0.0-x64.exe` + `SHA256SUMS.txt` | Fait (`artifacts/`) |
| README, guide d'utilisation, guide de restauration, installation, catalogue, benchmarks, overclocking, design, idées, dépendances et licences, notices tierces | Fait (`docs/`) |
| Rapport d'audit, rapport de tests, état des fonctionnalités, présente vérification | Fait (`docs/AUDIT.md`, `docs/RAPPORT-TESTS.md`, `docs/FONCTIONNALITES.md`) |
