# Benchmarks, vérification et estimation des FPS

## 1. Benchmark rapide

| Mode | Durée | Usage |
|---|---|---|
| Standard | ≈ 4 à 5 min | Mesure de référence, avant/après manuel |
| Express | ≈ 1 min 30 | Utilisé automatiquement par l'option « Mesurer avant et après » de l'optimisation |

| Étape | Mesure | Unité | Répétitions (std / express) | Seuil de bruit minimal |
|---|---|---|---|---|
| CPU 1 cœur | unités de calcul vérifiées / s | u/s | 6 × 4 s / 3 × 2 s | 2 % |
| CPU multicœur | idem, tous les threads | u/s | 6 × 4 s / 3 × 2 s | 2 % |
| Charge prolongée | débit par fenêtre de 5 s sur 60 s (20 s) ; ratio fin/début | u/s, % | 12 / 4 fenêtres | 3 % |
| Bande passante RAM | copie de 256 Mo | Go/s | 9 / 5 | 3 % |
| Latence RAM | parcours aléatoire de 256 Mo (anti-cache) | ns | 4 / 2 | 3 % |
| Stockage | écriture/lecture séquentielles d'un fichier de 512 Mo **sans cache Windows** ; lectures aléatoires 4 Ko file d'attente 1 | Mo/s, IOPS, µs | 2 / 1 passes, 4 / 2 × 3 s | 8–15 % |
| GPU | calcul Direct3D 11 vérifié + débit VRAM | passes/s, Go/s | 3 × 15 s / 2 × 10 s | 3–4 % |
| Réactivité | retard de réveil d'un minuteur haute résolution de 1 ms (DPC/ISR, pilotes) | µs (médiane, 99e centile) | 4 × 3 s / 3 × 2 s | 15–20 % |

Pendant la charge, les capteurs sont relevés chaque seconde : température CPU/GPU (si disponibles),
fréquence CPU effective, utilisation, compteur Windows « % Performance Limit » et ses drapeaux.

**Bridage** : *détecté* seulement si le débit baisse d'au moins 7 % **et** que la température dépasse 90 °C
**et** qu'une limitation est signalée ; sinon *probable (thermique)*, *probable (puissance/firmware)* ou *non évaluable*.

**Progression** : chaque étape est bornée dans le temps ; le pourcentage affiché est le temps écoulé sur le temps prévu
(jamais une progression simulée).

## 2. Comparaison avant / après

Pour chaque mesure : médiane, coefficient de variation, intervalle de confiance à 95 % de la moyenne, test t de Welch.
Seuil de bruit = max(seuil minimal du composant, 2 × la variation observée).

| Verdict | Condition |
|---|---|
| Amélioration / dégradation confirmée | écart > seuil de bruit **et** p < 0,05 (confiance élevée si p < 0,01 et ≥ 5 mesures) |
| Dans le bruit | écart < seuil de bruit |
| À confirmer | écart > seuil mais non significatif, ou une seule mesure |

**Indice relatif** : moyenne géométrique des rapports après/avant par composant (les écarts dans le bruit comptent pour 1),
× 100. Ce n'est jamais un score absolu : 100 = identique.

La comparaison signale un changement de pilote graphique ou une mise à jour Windows entre les deux mesures
(détection de régression après mise à jour).

## 3. Vérification & réparation automatique

1. Relecture de chaque modification appliquée.
2. Valeur non conservée : réécriture au plus **deux fois**, puis restauration de la valeur d'origine.
3. Si « Mesurer avant et après » est actif : comparaison des deux benchmarks express. Une dégradation confirmée
   (confiance ≥ moyenne) d'un composant entraîne la restauration des seules modifications pouvant l'affecter
   (les réglages réseau, confidentialité ou confort ne sont jamais incriminés), puis une contre-mesure :
   - la dégradation disparaît → les modifications restent annulées ;
   - elle persiste → elles n'en étaient pas la cause et sont réappliquées **une seule fois**.
4. Chaque décision est inscrite dans l'historique de la modification et dans un rapport (page Restauration).

Après un redémarrage, les modifications qui en exigeaient un sont relues automatiquement.

## 4. Estimateur de FPS

Bibliothèque : 91 jeux (`catalog/knowledge/games.json`), 114 cartes graphiques et 88 processeurs
(`catalog/knowledge/hardware-performance.json`). GTA VI est exclu tant qu'aucune version PC n'est confirmée.

```
FPS limité GPU = FPS de référence (RTX 4070, 1440p, Élevé)
                 × indice GPU / indice RTX 4070
                 × (pixels / pixels 1440p) ^ (−k du jeu)
                 × facteur de préréglage (Bas 1,75 · Moyen 1,35 · Élevé 1 · Ultra 0,8)
FPS limité CPU = plafond CPU de référence (Ryzen 7 7800X3D) × indice CPU / indice 7800X3D
                 × facteur de préréglage CPU × facteur mémoire (simple canal, XMP inactif, RAM insuffisante)
FPS            = minimum « doux » des deux (p = 4), puis pénalité VRAM et plafond d'images du jeu
```

- **Plage** : ± 20 % par défaut, élargie si le matériel est identifié approximativement, si le processeur est inconnu,
  si le jeu est très variable (bac à sable, mods, serveurs) ; resserrée pour les jeux à scène reproductible.
- **Origine affichée** : mesure réelle · calibrée par votre mesure · théorique · extrapolée (matériel similaire) · données insuffisantes.
- **Confiance** : jamais « élevée » sans mesure réelle sur la machine.
- **Mesure réelle** : une capture en jeu associée au jeu (page Benchmark) remplace l'estimation à ces réglages et
  calibre les autres réglages (facteur borné entre 0,5 et 2).

**Gain après optimisation** : uniquement à partir des écarts *confirmés* du benchmark rapide avant/après, pondérés par
la part CPU et GPU du jeu (élasticités du minimum doux) et sa sensibilité à la mémoire. Sans benchmark : « gain non
estimé ». Si aucune optimisation appliquée ne peut influencer les FPS : 0 % annoncé explicitement. Une modification
réseau ne produit jamais de gain de FPS.

**Trois jeux mis en avant** : un jeu compétitif sensible au processeur, un jeu solo exigeant (≥ 30 i/s estimés),
un jeu populaire, en privilégiant les jeux déjà mesurés et ceux dont les données sont reproductibles.

## 5. Laboratoire

Tests séparés : processeur, mémoire, carte graphique/VRAM, stockage, réactivité, calcul de type IA (multiplication de
matrices FP32 — indicateur, pas un modèle d'IA réel), réseau local (passerelle), latence Internet (uniquement avec
consentement), stress combiné CPU + GPU arrêté automatiquement au-delà de 95 °C (CPU) ou 90 °C (GPU), limité à
2 minutes si les températures ne sont pas lisibles.

Outils étudiés et non embarqués (licences et redistribution) : Intel PresentMon (MIT, détecté et vérifié par signature
s'il est installé), Microsoft DiskSpd (MIT, non embarqué : test de stockage interne équivalent), ONNX Runtime (MIT,
non embarqué : ~20 Mo+ et un modèle à fournir), llama.cpp (MIT, non embarqué : modèles volumineux), Blender
Benchmark, OCCT et MemTest86 (licences propriétaires ou redistribution non autorisée), Windows Performance Recorder
(fourni avec le Windows ADK).
