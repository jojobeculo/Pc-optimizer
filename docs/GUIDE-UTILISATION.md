# Guide d'utilisation de FrameCore 2

## 1. Premier lancement

1. Ouvrez **FrameCore** (menu Démarrer). L'application fonctionne **sans droits administrateur** : l'analyse ne modifie rien.
2. Le diagnostic démarre seul. Le **Tableau de bord** affiche :
   - la jauge « réglages du profil Performances actifs » (ex. 4/23) — un décompte factuel, pas un score inventé ;
   - l'état général et les constats classés *critique*, *avertissement*, *suggestion*, chacun avec son action ;
   - vos composants, avec les valeurs en direct ;
   - un bandeau si une optimisation a été interrompue (coupure, plantage) ou après un redémarrage.
3. Une valeur illisible est affichée « indéterminée ». **Matériel → Fiabilité des données** indique pour chaque valeur si
   elle est mesurée, déclarée par le matériel, déduite (probable) ou indisponible, et pourquoi.

## 2. Optimiser (mode simple)

Page **Optimisation** — quatre niveaux :

| Niveau | Contenu |
|---|---|
| **Équilibré** | Réglages confirmés sans compromis : réactivité, suppression du superflu, réparations |
| **Performances** | Ajoute les réglages probables à faible risque (alimentation, services de télémétrie, confidentialité, périphériques) |
| **Performance maximale** | Ajoute les réglages les plus poussés ; les réglages expérimentaux et ceux qui ont une contrepartie sont listés à part, **jamais cochés d'office** |
| **Extrême** | Pour un PC qui sert **uniquement à jouer** : tout « Performance maximale » (cette fois **tout coché**, expérimentaux compris), plus des réglages qui échangent un peu de sécurité ou de confort contre des performances. Un bandeau rouge et un **avertissement listant chaque contrepartie** précèdent l'application |

### Ce que le niveau Extrême ajoute

| Réglage | Contrepartie |
|---|---|
| Intégrité de la mémoire (HVCI) désactivée — recommandé par Microsoft pour les PC de jeu qui veulent le maximum | protection en moins contre les pilotes malveillants ; certains anti-triches (Valorant/Vanguard, FACEIT) peuvent l'exiger : à réactiver alors depuis Restauration |
| Analyses planifiées de Microsoft Defender limitées à 20 % du processeur (50 % par défaut) | une analyse complète dure plus longtemps |
| Dossiers des jeux (Steam, Epic, Riot, Xbox, EA, Ubisoft) exclus de l'analyse en temps réel | un fichier piégé placé dans ces dossiers ne serait pas détecté à l'ouverture ; FrameCore ne retire à la restauration **que** les dossiers qu'il a ajoutés |
| Microsoft Edge : démarrage anticipé et exécution en arrière-plan désactivés | Edge s'ouvre un peu moins vite |
| Hibernation, démarrage rapide, SysMain, indexation de la recherche, stationnement des cœurs, état minimal du processeur, minuterie globale | voir chaque fiche dans [CATALOGUE.md](CATALOGUE.md) (ex. recherche de fichiers plus lente, consommation plus élevée) |
| Programmes au démarrage : toutes les applications facultatives cochées (sauf programmes de mise à jour) | elles se lancent à la demande |

**Jamais touchés, même en Extrême** : protection en temps réel de Defender, pare-feu, Windows Update, Secure Boot, TPM,
BitLocker, protections du processeur (mitigations), pilotes et composants anti-triche, audio, périphériques.

**Prêter ou donner le PC** : *Restauration → Tout restaurer* remet les réglages d'avant FrameCore (même des mois plus
tard, tant que l'application est installée), puis choisissez **Équilibré** pour une optimisation légère.

1. Cliquez sur un niveau : le **plan propre à votre PC** apparaît (rien n'est encore modifié) avec, pour chaque réglage,
   la valeur actuelle et la valeur proposée, les réglages déjà en place, ceux exclus (avec la raison : portable, Ryzen X3D
   bi-CCD, version de Windows, conflit avec un autre réglage…), et les actions à faire vous-même (BIOS, pilote).
2. Décochez ce que vous ne voulez pas. Les programmes au démarrage proposés à la désactivation sont listés à part ;
   les programmes de mise à jour ne sont **jamais cochés d'office** (correctifs de sécurité, aucun effet mesurable sur
   les FPS) et ceux des navigateurs, d'Acrobat et de Java sont conservés.
3. Options : point de restauration Windows ; **Mesurer avant et après** (benchmark express, ≈ 3 min au total, recommandé).
4. **Appliquer la sélection** (ou **Appliquer…** en haut du plan : la page descend d'elle-même vers la progression,
   puis vers le résultat). Windows demande une seule fois l'autorisation administrateur.
5. La progression suit les vraies étapes : mesure avant → point de restauration → application (x/N) → vérification →
   mesure après.
6. Le **Résultat** indique si les mesures avant/après ont bien été faites (ou pourquoi elles ne l'ont pas été), signale en
   orange les réglages **non appliqués faute d'autorisation administrateur** (bouton « Terminer » pour les appliquer), puis ce qui est appliqué, ce qui a été **relu et vérifié**, ce que Windows n'a pas conservé
   (réécrit au plus deux fois puis annulé), l'effet mesuré (indice après/avant) et trois jeux avec leurs FPS avant → après.

## 2 bis. Enquête détective et modes Rapide / Complet

Page **Enquête** : un seul écran d'autorisation liste tout ce qui sera lu, puis FrameCore enquête (≈ 1 min en rapide,
≈ 2 min en approfondi) — rien n'est modifié pendant l'enquête :

| Volet | Ce qui est mesuré |
|---|---|
| Processus | chaque processus en cours, sa consommation et surtout ses **réveils du processeur par seconde** (le coût caché des centaines de processus « à 0 % »), classé **nuisible / inutile / peu utile / utile / très utile / primordial** |
| Latence interne | trace du noyau Windows : le plus long appel (DPC/ISR) de **chaque pilote**, comme LatencyMon |
| Réseau | ping vers la box puis vers Internet (d'où vient la latence), gigue, pertes, carte en 100 Mb/s, Wi-Fi, VPN, trafic en arrière-plan, test sous charge (bufferbloat) |
| Stabilité | écrans bleus avec leur cause probable, redémarrages brutaux, erreurs matérielles WHEA, pilote graphique redémarré, jeux qui plantent, erreurs disque |
| Matériel / stockage | carte graphique sur moins de lignes PCIe que prévu, Resizable BAR, SSD plein / usé / chaud |
| Avant / maintenant | la première enquête sert de référence : EXPO perdu, nouveaux programmes au démarrage, plus de réveils, disque qui se remplit, pilote changé, benchmarks et températures qui se dégradent |

Chaque constat a ses chiffres, une explication et, quand c'est possible, un bouton de correction (fermer et retirer
du démarrage, mettre en retrait, profil jeu, vider le cache des shaders, test mémoire) ; **Tout corriger
automatiquement** liste puis applique.

**Modes** : chaque niveau (Équilibré, Performances, Performance maximale, Extrême) se lance en **Rapide** (plan +
mesure express) ou en **Complet** : enquête intégrale, benchmark précis avant, plan + traitement des processus selon le
niveau, vérification, overclocking GPU automatique si coché, latence revérifiée, benchmark après et tableau avant →
après (indice, latence des pilotes, nombre de processus et réveils du processeur).

**Profil jeu automatique** (page Jeux) : quand un jeu se lance, les programmes dispensables pour le niveau choisi
passent en priorité minimale et le plan d'alimentation le plus rapide est activé ; tout est rétabli à la fermeture du
jeu. FrameCore doit rester ouvert (il peut être réduit).

## 3. Jeux & FPS

- **Sélection pour votre configuration** : trois jeux pertinents avec FPS estimés avant et après, plage, limitation
  principale (processeur, carte graphique, VRAM, plafond du jeu), niveau de confiance et **origine du chiffre**
  (mesure réelle, calibrée, théorique, extrapolée, données insuffisantes).
- **Explorer les 91 jeux** : recherche, genres, tri (popularité, FPS, gain, nom), fiche détaillée.
- Résolution et préréglage modifiables pour toute la page.
- Le gain « après » vient uniquement de vos benchmarks rapides avant/après (écarts statistiquement confirmés). Une
  modification réseau ou de confidentialité n'est jamais présentée comme un gain de FPS.
- Pour une valeur exacte : **Benchmark → Capture en jeu**, choisissez le jeu dans la liste « Jeu de la bibliothèque » ;
  la mesure remplace l'estimation.


Le chiffre « après » ne vient que de mesures : il ne change que si le benchmark constate un gain réel du processeur, de
la mémoire ou de la carte graphique. La plupart des optimisations Windows améliorent surtout la régularité (1 % low) et
la réactivité en jeu, ce qu'un test de calcul ne voit pas : le bouton **Mesurer en jeu (avant / après)** ouvre la capture
en jeu avec le jeu déjà choisi. Avant toute optimisation, la page indique « Aucune optimisation n'est encore appliquée »
au lieu d'un gain.

## 4. Benchmark

- **Benchmark rapide** (standard ≈ 4–5 min, express ≈ 1 min 30, **complet ≈ 12 min** avec stress processeur + carte
  graphique, uniquement quand vous le lancez) : processeur 1 cœur / multicœur / charge prolongée,
  mémoire, stockage, carte graphique, réactivité, températures et bridage. Chaque mesure est répétée (médiane,
  variation, intervalle de confiance). **Comparaison avant / après** avec interprétation : amélioration ou dégradation
  confirmée, dans le bruit, à confirmer. **Partager (HTML)** enregistre la comparaison affichée dans une page à
  envoyer à un ami ou sur un forum (sans nom de machine, nom d'utilisateur ni numéro de série).
- **Laboratoire** : tests séparés (y compris réseau local, latence Internet avec votre accord, calcul de type IA et
  stress combiné CPU + GPU avec arrêt automatique en cas de surchauffe).
- **Capture en jeu** : frametimes réels (ETW intégré, Intel PresentMon vérifié par signature, ou import CSV),
  1 % / 0,1 % low, passages répétés, comparaison A/B, analyse des limitations.

Méthodes détaillées : [BENCHMARKS.md](BENCHMARKS.md).

## 5. Matériel, Pilotes, Overclocking

- **Matériel** : détail de chaque composant, mesures en direct, fiabilité des données, procédure guidée XMP/EXPO.
- **Pilotes** : versions, signataires, mises à jour Windows Update, liens officiels, **historique des versions** sur ce PC.
- **Overclocking** : carte graphique NVIDIA/AMD via les interfaces officielles (manuel, automatique progressif, test de
  stabilité, réglages d'usine) ; processeur : test de stabilité et conseils BIOS. Voir [OVERCLOCKING.md](OVERCLOCKING.md).

## 6. Restauration

Tout ce que FrameCore a modifié, avec la valeur exacte d'origine : restaurer un élément, une catégorie (seules les
catégories qui ont encore des modifications actives sont proposées, avec leur nombre), un profil (chaque profil
appliqué indique ce qui est encore actif ou « entièrement restauré ») ou tout ; **Vérifier et réparer maintenant** (après une mise à jour Windows par exemple) ; reprise d'une optimisation
interrompue ; rapports de vérification. Voir [GUIDE-RESTAURATION.md](GUIDE-RESTAURATION.md).

## 7. Mode Pro

Chaque réglage individuellement par catégorie : description, **objectif**, mécanisme, valeur actuelle et proposée, impact,
ce qu'il peut influencer (et s'il n'a aucune raison de modifier les FPS), confiance, risque, compatibilité, persistance,
conflits, méthode de vérification et de restauration, sources ; Appliquer / Restaurer. Également : programmes au
démarrage et services tiers (avec le **temps de démarrage de Windows** mesuré par Windows lui-même : durée jusqu'au bureau, tendance, et ce qui ralentit le démarrage), **carte graphique par application** (catégorie Carte graphique : choisir « hautes
performances » pour un jeu sur un portable à deux cartes graphiques, restaurable), profils personnalisés exportables, surveillance temps réel et **Réglages écartés** (astuces
répandues que FrameCore refuse, avec la raison).

## 8. Paramètres

Langue (français/anglais), thème sombre ou clair, point de restauration automatique, capteurs avancés (PawnIO installé
par vous), PresentMon, rapport de diagnostic HTML/JSON anonymisable, vérification des mises à jour (désactivée par défaut).

## 9. Accessibilité

Navigation complète au clavier (Tab, flèches, Entrée), focus visible, textes redimensionnés avec la mise à l'échelle de
Windows, animations désactivées si « Effets d'animation » est désactivé dans Windows.
