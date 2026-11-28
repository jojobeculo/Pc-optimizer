# Rapport de tests — FrameCore 2.1.0

Deux environnements :

- **Conteneur Linux** (développement) : compilation, tests unitaires, construction de l'installateur. Pas de Windows.
- **Machine virtuelle Windows jetable** (GitHub Actions `windows-latest`, détruite après chaque exécution) :
  Windows Server 2025 Datacenter 24H2 (build 26100), AMD EPYC, 16 Go, pilote d'affichage de base Microsoft
  (aucun GPU), aucun capteur de température, compte administrateur. Les tests qui **modifient** Windows ne
  s'exécutent que sur cette machine (variable `FRAMECORE_DISPOSABLE_VM=1`), jamais sur un PC personnel.

Chaque modification du code relance toute la chaîne (`.github/workflows/windows-ci.yml`). Les chiffres
ci-dessous proviennent de la dernière exécution complète ; les captures d'écran, journaux et rapports JSON sont
publiés comme artefacts de chaque exécution.

## 1. Synthèse

| Vérification | Résultat |
|---|---|
| Compilation Release des 15 projets + 2 projets de tests | ✅ 0 erreur, 0 avertissement |
| Tests unitaires (xUnit) | ✅ 163 / 163 (Linux et Windows) |
| Test d'intégration « détection matérielle » sur Windows réel | ✅ |
| Test d'intégration « aller-retour du catalogue » sur Windows réel | ✅ 53 optimisations appliquées, relues, restaurées : état d'origine retrouvé à l'identique pour toutes ; 1 « non applicable » correctement détectée ; 16 incompatibles avec la VM ignorées ; 0 non effective |
| Aller-retour via le processus élevé (chemin des comptes non administrateurs) | ✅ 33 optimisations exigeant des droits : 19 appliquées et vérifiées, 14 détectées « déjà en place » (rien écrit) ; état d'origine retrouvé à l'identique pour les 33 (connexion 0,5 s, total 2,4 s) |
| Visite automatique de l'interface (≈ 110 écrans, thèmes sombre/clair, FR/EN, petite fenêtre) | ✅ 0 erreur de liaison WPF, 0 exception d'interface (administrateur, utilisateur standard, application installée) |
| Mise à l'échelle et redimensionnement : 10 pages à 1366×768 / 125 % et 1920×1080 / 150 % | ✅ aucun contenu coupé (contrôle automatique) |
| Parcours utilisateur réels (benchmark, application d'un profil, jeux, vérification, restauration, laboratoire, mode Pro, démarrage) | ✅ voir § 3 |
| Installateur : installation silencieuse, lancement de l'application installée, désinstallation | ✅ voir § 5 |
| Paquet sans données par machine ni chemin de la machine de build (contrôle bloquant du build) | ✅ |
| Audit d'accessibilité automatique (noms accessibles, taille des cibles) + parcours au clavier | ✅ voir § 6 |

## 2. Tests unitaires (163)

| Fichier | Contenu principal |
|---|---|
| `CatalogTests` | Chargement et validation du catalogue (références, conflits symétriques, zones d'impact, opérations ponctuelles), textes FR/EN complets, règles des profils, aucune cible protégée (Defender, pare-feu, Windows Update, VBS/HVCI, Secure Boot, BitLocker, mitigations) hormis les 3 contreparties du niveau Extrême (réservées à ce niveau, consentement, réversibles), plan Extrême = Performance maximale + ses réglages, gestionnaires connus |
| `EngineTests` | Application/restauration exacte (valeurs absentes comprises), rollback incluant l'action en échec, journal écrit avant modification, refus d'un réglage incompatible, plans sans conflit, relecture après application |
| `AuditRegressionTests` | Non-régression des constats de l'audit (journal interrompu restaurable, reprise sans double application, profils renommés, culture, progression) |
| `QuickBenchTests` (+ niveaux GPU dans `AnalysisTests`) | Médiane, MAD, CV, intervalles de confiance, test de Welch, seuil de bruit, indice relatif, progression monotone, détection du bridage, comparaison avec changement de pilote/Windows ; Vérification & Réparation (2 réécritures puis restauration, régression mesurée qui disparaît / persiste, réseau et confidentialité jamais incriminés) |
| `GameEstimatorTests` | Correspondance GPU/CPU (portable vs bureau, F/KF, APU), modèle limité GPU/CPU, pénalité de VRAM, plafonds, plages d'incertitude, origine de l'estimation, gain projeté uniquement à partir de mesures confirmées, sélection de 3 jeux |
| `BootPerformanceTests` | Lecture des événements Windows de démarrage (format XML réel, secondes, indicateurs), refus des valeurs manquantes, classement des ralentissements (application, pilote, service, périphérique), médiane robuste aux démarrages aberrants, tendance, principaux responsables |
| `CombinedStressTests` | Stress combiné du benchmark complet : arrêt immédiat au seuil thermique, durée limitée sans capteurs, erreurs de calcul = instable |
| `DefenderHandlerTests` | Niveau Extrême : plafond des analyses planifiées et exclusion des dossiers de jeux appliqués puis restaurés exactement (Defender simulé), « 0 = aucune limite » (constaté sur Windows réel), rien de deviné sans droits, non applicable si Defender n'est pas l'antivirus |
| `LongTermRestoreTests` | Restauration des mois plus tard : liste figée de toutes les actions de la version 2.0.0, toujours acceptées par le processus élevé pour la restauration ; action retirée restaurable mais jamais applicable ; journal vieux de 7 mois rechargé et restauré sans catalogue |
| `CpuOcGuideTests` | Guide d'overclocking CPU : famille reconnue (jamais devinée ; rien pour portable/VM), 3 niveaux par famille, chaque réglage situé dans le BIOS des 4 marques en FR/EN, aucune tension dangereuse |
| `LocalizationConcurrencyTests` | Traductions chargées et lues depuis 64 tâches parallèles sans erreur (défaut réel de concurrence trouvé sur Windows en CI, corrigé) |
| `CpuProcedureTests` | Procédure pas à pas affichée pour un Ryzen de bureau (paliers, retour aux réglages d'usine) ; aucune procédure inventée pour un processeur non identifié ou une machine virtuelle |
| `GpuPreferenceTests` | Format de Windows (`GpuPreference=N;`), refus des chemins invalides, application journalisée puis restauration exacte sans toucher aux autres applications |
| `XamlConsistencyTests` | Toute ressource XAML utilisée existe là où elle est utilisée ; toute clé de traduction utilisée par les vues et le code existe en français et en anglais |
| `AnalysisTests` | EDID, références de barrettes, canaux mémoire, identification CPU/GPU/carte mère, cycle de vie Windows, sortie `bcdedit` indépendante de la langue, compteurs GPU, PresentMon 1.x/2.x, statistiques d'images (1 % / 0,1 % low), goulots d'étranglement, base de connaissances du démarrage (anti-cheats protégés, mises à jour de sécurité conservées), désactivation sans suppression, protocole du processus élevé et validation des requêtes |

## 3. Parcours utilisateur réels sur Windows (machine jetable)

| Parcours | Résultat mesuré |
|---|---|
| Premier lancement → fenêtre affichée | 0,9 s |
| Diagnostic complet (programmes au démarrage analysés en arrière-plan) | 5,8 s (17,4 s avant optimisation de l'analyse) |
| Benchmark rapide « express » | 75 s ; CPU 1 cœur/tous cœurs/charge prolongée, mémoire, stockage, réactivité, médianes + IC 95 % ; GPU « non mesuré » (pas de GPU) ; aucun bridage détecté |
| Profil **Équilibré** appliqué avec mesure avant/après | 12 modifications appliquées, 0 échec ; 9/9 vérifiées actives après relecture ; point de restauration indisponible sur Windows Server (message explicite) |
| Effet mesuré | indice 97,7 : baisse limitée au stockage, attribuée à un facteur externe car aucune modification appliquée ne touche le stockage — aucune restauration inutile |
| Vérification & Réparation, puis **Tout restaurer** | 18 actions restaurées et vérifiées ; 0 modification active ensuite |
| Laboratoire : réactivité, calcul FP32 | exécutés, résultats affichés |
| Jeux & FPS sur la VM | page en mode « carte graphique non identifiée » : aucune estimation inventée |
| Mode Pro : un réglage appliqué avec son bouton, puis sa catégorie restaurée | appliqué → « déjà en place » ; après restauration : « non appliqué », 0 modification active |
| Programmes au démarrage : désactiver puis réactiver une entrée | état relu correct après chaque action |
| Carte graphique par application (notepad.exe) | réglée « hautes performances », listée, puis rendue au choix de Windows (entrée supprimée) |
| Comparaison avant/après partageable | page HTML exportée, sans nom de machine ni numéro de série |
| Mémoire / CPU de l'application au repos | 186 Mo (ensemble de travail), 1,1 % d'un cœur en moyenne |

## 4. Chemins non administrateur

Sur un PC personnel, l'application tourne **sans droits administrateur** : chaque modification qui en exige
passe par un processus élevé (UAC), via un canal nommé dont les requêtes sont revalidées contre le catalogue.
L'exécuteur CI étant administrateur, deux tests forcent ce chemin :

- **Aller-retour via le processus élevé** : toutes les optimisations compatibles qui exigent des droits sont
  appliquées, vérifiées et restaurées à travers le canal, puis l'état est comparé à l'état d'origine
  (rapport `workerRoundTrip`).
- **Visite en utilisateur standard** (`runas /trustlevel:0x20000`, jeton sans le groupe Administrateurs) : le
  diagnostic (2,8 s) et toutes les pages fonctionnent sans droits ; 0 erreur de liaison ; seuls 2 réglages restent
  illisibles sans droits (options de démarrage `bcdedit`) — ils sont relus au moment d'appliquer et ne sont pas
  modifiés s'ils sont déjà en place. Les demandes d'élévation sont refusées (comme un utilisateur qui refuse l'UAC)
  et l'application continue sans planter.

Non testable ici : l'affichage réel de la fenêtre UAC (le runner n'a pas d'utilisateur interactif).

## 5. Installateur

| Contrôle | Résultat |
|---|---|
| Installation silencieuse (`/S`) | code 0 |
| Fichiers, catalogue, raccourci du menu Démarrer, entrée « Applications » (FrameCore 2.1.0, ≈ 148 Mo) | présents |
| Lancement de l'application **installée** (visite complète) | code 0, rapport produit |
| Désinstallation silencieuse | code 0 ; dossier supprimé ; entrée supprimée ; données utilisateur conservées (journal de restauration) |

## 6. Accessibilité

- Audit automatique sur chaque page : contrôles atteignables au clavier sans nom accessible, cibles de moins de
  22 px. Les constats ont été corrigés (en-têtes « Détails techniques » agrandis, noms accessibles ajoutés).
- Parcours au clavier (Tab) de chaque page : le focus reste visible (anneau de focus) et parcourt toute la page.
- Contrastes calculés pour les deux thèmes (WCAG AA) : voir `docs/DESIGN.md`.
- Animations désactivées automatiquement si « Effets d'animation » est désactivé dans Windows.

## 7. Non testé (matériel absent) — voir aussi `docs/FONCTIONNALITES.md`

| Fonction | Raison |
|---|---|
| Overclocking NVIDIA (NVML) / AMD (ADLX) | aucune carte graphique — **à considérer comme expérimental** |
| Partie GPU du benchmark, stress combiné, VRAM | aucune carte graphique |
| Températures, puissance, capteurs avancés | aucun capteur exposé par la VM |
| Capture ETW / PresentMon d'un vrai jeu, calibration de l'estimateur | aucun jeu installable sur la VM |
| Point de restauration Windows | indisponible sur Windows Server |
| Fenêtre UAC réelle, redémarrage réel entre application et vérification | pas de session interactive |

## 8. Recommandations pour la première utilisation sur un PC

1. Laisser l'option « point de restauration » activée la première fois.
2. Lancer un **benchmark rapide** avant d'optimiser (ou cocher « mesurer avant/après »).
3. Commencer par le profil **Équilibré**, vérifier, puis essayer **Tout restaurer** pour constater le retour arrière.
4. En cas de problème : *Paramètres → Rapport de diagnostic* (anonymisé par défaut) et *Restauration → Tout restaurer*.
