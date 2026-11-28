# État des fonctionnalités — FrameCore 2.0

Légende : **Testée** = vérifiée automatiquement (tests unitaires et/ou exécution réelle sous Windows en CI) ·
**Partielle** = une partie vérifiée, une partie dépend d'un matériel absent · **Non testable ici** = code écrit d'après la
documentation officielle, matériel indisponible dans la machine virtuelle de test · **Expérimentale** = proposée
seulement avec avertissement et confirmation explicite.

Environnement de test Windows : machine virtuelle GitHub Actions, Windows Server 2025 Datacenter 24H2 (build 26100),
AMD EPYC, 16 Go, pilote d'affichage de base Microsoft (pas de GPU), droits administrateur, aucun capteur de température.

| Fonctionnalité | État | Comment c'est vérifié |
|---|---|---|
| Installation, raccourcis, entrée « Applications », désinstallation (données conservées) | Testée | installation silencieuse de l'installateur publié, lancement de l'app installée, désinstallation, contrôles fichiers/registre |
| Démarrage, navigation, rendu de toutes les pages (thèmes sombre/clair, FR/EN, petite fenêtre) | Testée | visite automatique (~90 captures) en administrateur **et** en utilisateur standard, 0 erreur de liaison WPF, 0 exception d'interface ; ressources XAML et traductions vérifiées par des tests |
| Détection matérielle (CPU, RAM, carte mère, BIOS, stockage, écrans, réseau, Windows, sécurité) | Testée (sur VM) | test d'intégration + profil exporté ; valeurs propres au matériel physique (SPD, SMART, VRR) non vérifiables sur VM |
| Fiabilité des données (mesuré / déclaré / déduit / indisponible) | Testée | tests unitaires + écran sur VM |
| Application et restauration exacte des optimisations du catalogue | Testée | test d'intégration : chaque réglage compatible appliqué, relu, restauré, état comparé à l'origine |
| Statut « non applicable » (paramètre absent de la machine) | Testée | constaté et corrigé sur la VM (réglages processeur, plan Performances ultimes) |
| Profils Équilibré / Performances / Performance maximale, plan avant application, conflits | Testée | tests unitaires + parcours réel (profil Équilibré appliqué sur la VM) |
| Niveau Extrême (PC dédié au jeu) : avertissement, contreparties listées, tout restaurable | Partielle | règles du catalogue testées (contreparties de sécurité réservées à Extrême, consentement, réversibilité) ; réglages Defender appliqués puis restaurés via son interface officielle (tests avec un Defender simulé + aller-retour réel sur la VM) ; intégrité de la mémoire inactive sur la VM, donc non modifiée |
| Conseils de réglages en jeu (10 jeux compétitifs : Fortnite, Valorant, CS2…) | Testée (affichage) | textes affichés dans la fiche du jeu ; FrameCore ne modifie jamais les fichiers des jeux |
| Vérification & réparation (relecture, ≤ 2 réécritures, restauration) | Testée | tests unitaires (Windows « oublie » la valeur, valeur conservée au 2e essai) + rapport réel 9/9 vérifiés |
| Retour arrière sur régression de performance mesurée | Partielle | tests unitaires (régression qui disparaît / qui persiste) ; aucune régression réelle provoquée sur la VM |
| Reprise d'une optimisation interrompue, vérification après redémarrage | Partielle | logique testée (tests unitaires du journal) ; coupure de courant réelle non simulée |
| Point de restauration Windows | Non testable ici | indisponible sur Windows Server (message explicite) ; code appelé sur Windows 11 |
| Élévation UAC à la demande (processus séparé, canal nommé) | Partielle | canal réel testé sur Windows : 33 optimisations appliquées, vérifiées et restaurées à l'identique via le processus élevé ; application en utilisateur standard sans plantage ; fenêtre UAC elle-même non affichable sur le runner |
| Benchmark rapide (CPU, mémoire, stockage, réactivité, bridage) | Testée | exécuté réellement (express) sur la VM ; statistiques et progression couvertes par des tests |
| Benchmark rapide — partie GPU | Non testable ici | pas de GPU matériel sur la VM (mesure marquée « non mesuré ») |
| Températures, fréquences GPU, puissance | Non testable ici | aucun capteur exposé par la VM ; affichés « indéterminés » (jamais 0) |
| Comparaison avant/après partageable (HTML sans données personnelles) | Testée | export réel sur Windows pendant la visite |
| Comparaison avant/après, indice relatif, détection de changement de pilote/Windows | Testée | tests unitaires + comparaison réelle avant/après optimisation (indice 102,3, aucune dégradation confirmée) |
| Laboratoire : réactivité, calcul de type IA | Testée | exécutés sur la VM |
| Laboratoire : stockage, mémoire, CPU, réseau local | Testée (logique) / Partielle | mêmes moteurs que le benchmark rapide ; réseau selon la passerelle de la VM |
| Laboratoire : stress combiné CPU + GPU avec arrêt thermique | Non testable ici | pas de GPU ni de capteurs |
| Benchmark « Complet » (≈ 12 min, stress combiné inclus, à la demande) | Partielle | arrêt thermique et verdict testés (tests unitaires) ; charge processeur réelle vérifiée sur Windows ; partie GPU et températures non testables sur la VM |
| Procédure pas à pas d'overclocking processeur | Testée (affichage) | adaptée au processeur détecté ; aucune action automatique, aucune tension modifiée |
| Estimateur de FPS (91 jeux), sélection de 3 jeux, explorateur | Partielle | modèle et correspondance matériel couverts par 13 tests ; sur la VM la carte graphique n'est pas identifiable → page en mode « données insuffisantes » (comportement attendu) |
| Calibration par capture réelle en jeu | Partielle | logique testée ; capture ETW d'un vrai jeu impossible sur la VM |
| Capture en jeu (ETW, PresentMon, import CSV), 1 % / 0,1 % low | Partielle | statistiques et lecture CSV testées ; capture réelle non réalisable sans jeu |
| Overclocking GPU NVIDIA (NVML) / AMD (ADLX), réglage automatique, niveaux Léger / Modéré / Élevé / Personnalisé | Non testable ici — **à considérer comme expérimental** | algorithme et niveaux couverts par des tests (marges, limite de puissance relevée puis rendue en cas d'échec) ; aucune carte réelle |
| Guide d'overclocking CPU par niveau, chemins et illustrations du BIOS par marque (ASUS, MSI, Gigabyte, ASRock) | Testée (données + affichage) | données vérifiées par des tests ; écrans rendus sous Windows pour un profil Ryzen 5 7600 simulé ; aucune action automatique, rien n'est écrit dans le BIOS ; illustrations schématiques, pas de captures officielles |
| Résultats de benchmark graphiques (cartes par composant, lignes cliquables, nuage des répétitions, barres avant/après, évolution) | Testée (affichage) | ligne de mesure et ligne de comparaison ouvertes et capturées par la visite automatique sur Windows |
| Restauration des mois plus tard, y compris après mise à jour de FrameCore ; « Prêter ou donner ce PC » | Testée (logique) | tests : journal ancien restauré sans catalogue, actions des versions publiées toujours acceptées par le processus élevé |
| Test de stabilité CPU (calculs vérifiés + WHEA) | Testée (logique) | unité de calcul déterministe testée ; exécutions courtes sur la VM |
| Conseils CPU, procédure guidée XMP/EXPO | Testée (affichage) | règles testées ; aucune action automatique par conception |
| Pilotes : inventaire, Windows Update, historique des versions | Partielle | inventaire réel sur la VM ; installation via Windows Update non déclenchée |
| Analyse du temps de démarrage de Windows | Partielle | décodage testé sur le format réel des événements (tests unitaires) ; sur la VM de test (Windows Server) le journal n'existe pas : le message « aucune donnée disponible » est affiché, rien n'est inventé ; lecture d'un vrai historique non vérifiable ici |
| Programmes au démarrage : inventaire, classification, désactivation réversible | Testée | inventaire réel (analyse en arrière-plan) ; désactivation puis réactivation d'une entrée réelle sur Windows ; mises à jour de sécurité jamais proposées d'office |
| Rapport de diagnostic HTML/JSON | Testée (logique) | génération couverte ; non ouvert par la visite |
| Mise à jour de l'application (manifeste, empreinte, consentement) | Non testable ici | désactivée par défaut ; aucun serveur de mise à jour configuré |
| Carte graphique par application (réglage officiel de Windows, restaurable) | Testée | tests unitaires (lecture, validation, aller-retour exact) + réglage puis retour au choix de Windows sur Windows ; effet réel seulement sur un PC à deux cartes graphiques |
| Enquête détective (processus, latence des pilotes, réseau, stabilité, matériel, avant/maintenant) | Testée (logique) / Partielle | 10 tests unitaires sur la logique ; exécutée réellement sur la VM Windows (trace noyau, processus, réseau, journaux) ; sur un vrai PC de jeu les pilotes et processus seront différents |
| Classement des processus en 6 niveaux, actions selon le niveau | Testée (logique) | base de connaissances + classement automatique des inconnus ; processus protégés, jeux et fenêtres ouvertes jamais touchés (tests) |
| Modes Rapide / Complet pour chaque niveau | Partielle | déroulé compilé et relié aux mêmes briques testées ; parcours complet réel non exécuté sur la VM (durée) |
| Profil jeu automatique | Non testable ici | aucun jeu sur la VM ; logique de détection et de rétablissement relue |
| Analyse des saccades et limite d'images conseillée | Testée (logique) | motifs périodique / démarrage / aléatoire couverts par des tests |
| Mise à l'échelle DPI et redimensionnement | Testée | pages principales rendues à 1366×768 / 125 % et 1920×1080 / 150 % : aucun contenu coupé (contrôle automatique) |
| Accessibilité : focus visible, noms accessibles, tailles, clavier | Testée | audit automatique pendant la visite + contrastes calculés (WCAG AA) |

## Réglages expérimentaux

Proposés dans « Performance maximale » (jamais cochés d'office, avec confirmation spécifique) et cochés dans « Extrême » (après l'avertissement du niveau) :
Win32PrioritySeparation (marqué non recommandé), MMCSS (réactivité, limitation réseau, tâche « Games »), minuterie globale,
tick dynamique BCD, stationnement des cœurs, état minimal du processeur, préférence énergétique EPP, modération des
interruptions réseau, Power Throttling, plan Performances ultimes, SysMain, Windows Search. Détails dans
[CATALOGUE.md](CATALOGUE.md).

## Non disponible (et pourquoi)

| Demande | Raison |
|---|---|
| Activer XMP/EXPO depuis Windows | réglage du BIOS uniquement ; procédure guidée fournie |
| PBO, Curve Optimizer, limites de puissance Intel depuis l'application | pas d'interface publique sans pilote noyau ; conseils et outils officiels |
| Réglage GPU Intel Arc | pas d'API publique de réglage |
| Contrôle des ventilateurs | laissé au pilote et aux logiciels des fabricants |
| Inférence IA réelle (ONNX Runtime, llama.cpp) | dépendances et modèles volumineux non embarqués ; indicateur de calcul FP32 à la place |
| Débit Internet (téléchargement) | aucun serveur de test officiel et libre de droits embarqué ; latence, gigue et pertes mesurées avec consentement |
| GTA VI dans la bibliothèque | aucune version PC confirmée |
| Signature numérique de l'installateur | aucun certificat fourni |
