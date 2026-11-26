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

Page **Optimisation** — trois niveaux :

| Niveau | Contenu |
|---|---|
| **Équilibré** | Réglages confirmés sans compromis : réactivité, suppression du superflu, réparations |
| **Performances** | Ajoute les réglages probables à faible risque (alimentation, services de télémétrie, confidentialité, périphériques) |
| **Performance maximale** | Ajoute les réglages les plus poussés ; les réglages expérimentaux et ceux qui ont une contrepartie sont listés à part, **jamais cochés d'office** |

1. Cliquez sur un niveau : le **plan propre à votre PC** apparaît (rien n'est encore modifié) avec, pour chaque réglage,
   la valeur actuelle et la valeur proposée, les réglages déjà en place, ceux exclus (avec la raison : portable, Ryzen X3D
   bi-CCD, version de Windows, conflit avec un autre réglage…), et les actions à faire vous-même (BIOS, pilote).
2. Décochez ce que vous ne voulez pas.
3. Options : point de restauration Windows ; **Mesurer avant et après** (benchmark express, ≈ 3 min au total, recommandé).
4. **Appliquer la sélection**. Windows demande une seule fois l'autorisation administrateur.
5. La progression suit les vraies étapes : mesure avant → point de restauration → application (x/N) → vérification →
   mesure après.
6. Le **Résultat** indique ce qui est appliqué, ce qui a été **relu et vérifié**, ce que Windows n'a pas conservé
   (réécrit au plus deux fois puis annulé), l'effet mesuré (indice après/avant) et trois jeux avec leurs FPS avant → après.

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

## 4. Benchmark

- **Benchmark rapide** (standard ≈ 4–5 min, express ≈ 1 min 30) : processeur 1 cœur / multicœur / charge prolongée,
  mémoire, stockage, carte graphique, réactivité, températures et bridage. Chaque mesure est répétée (médiane,
  variation, intervalle de confiance). **Comparaison avant / après** avec interprétation : amélioration ou dégradation
  confirmée, dans le bruit, à confirmer.
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

Tout ce que FrameCore a modifié, avec la valeur exacte d'origine : restaurer un élément, une catégorie, un profil ou
tout ; **Vérifier et réparer maintenant** (après une mise à jour Windows par exemple) ; reprise d'une optimisation
interrompue ; rapports de vérification. Voir [GUIDE-RESTAURATION.md](GUIDE-RESTAURATION.md).

## 7. Mode Pro

Chaque réglage individuellement par catégorie : description, **objectif**, mécanisme, valeur actuelle et proposée, impact,
ce qu'il peut influencer (et s'il n'a aucune raison de modifier les FPS), confiance, risque, compatibilité, persistance,
conflits, méthode de vérification et de restauration, sources ; Appliquer / Restaurer. Également : programmes au
démarrage et services tiers, profils personnalisés exportables, surveillance temps réel et **Réglages écartés** (astuces
répandues que FrameCore refuse, avec la raison).

## 8. Paramètres

Langue (français/anglais), thème sombre ou clair, point de restauration automatique, capteurs avancés (PawnIO installé
par vous), PresentMon, rapport de diagnostic HTML/JSON anonymisable, vérification des mises à jour (désactivée par défaut).

## 9. Accessibilité

Navigation complète au clavier (Tab, flèches, Entrée), focus visible, textes redimensionnés avec la mise à l'échelle de
Windows, animations désactivées si « Effets d'animation » est désactivé dans Windows.
