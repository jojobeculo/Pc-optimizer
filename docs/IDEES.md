# Fonctionnalités : idées étudiées

Critères : intérêt réel pour l'utilisateur, faisabilité sans pilote noyau ni API non documentée, risques, priorité.
Statut : **fait** (dans cette version), **partiel**, **proposé** (non fait), **écarté**.

## Idées demandées

| Idée | Statut | Détail |
|---|---|---|
| Diagnostic intelligent hiérarchisé | fait | constats critique / avertissement / suggestion avec action directe |
| Historique matériel (températures, fréquences, performances) | partiel | historique des benchmarks rapides et des pilotes ; courbes temps réel ; pas encore d'historique continu des capteurs |
| Détection de throttling | fait | charge prolongée + « % Performance Limit » + températures |
| Analyse des applications au démarrage | fait | inventaire, classification, désactivation réversible |
| Gestionnaire de profils de jeux | proposé | voir n° 5 ci-dessous |
| Rapport de santé du PC | fait | export HTML/JSON anonymisable |
| Détection des régressions après mise à jour | fait | comparaison benchmark signalant un changement de pilote ou de build Windows ; vérification après redémarrage |
| Assistant de réparation réversible | partiel | réparations du catalogue (boost, état max CPU, TRIM, fichier d'échange, autotuning) ; pas de SFC/DISM automatisé |
| Analyse des processus | partiel | processus les plus gourmands en mémoire (page Matériel) |
| Analyse du temps de démarrage | proposé | journal Diagnostics-Performance (ID 100) : faisable sans droits élevés spéciaux |
| Centre de restauration | fait | historique, rapports, reprise, restauration par élément / catégorie / profil / tout |
| Conseiller matériel | partiel | limitation principale par jeu ; constats RAM (canal, XMP), stockage, écran |

## Dix idées supplémentaires

| # | Idée | Intérêt | Faisabilité | Risques | Priorité | Statut |
|---|---|---|---|---|---|---|
| 1 | **Mesure avant/après intégrée à l'optimisation** | Prouve l'effet réel, détecte une régression | Élevée (benchmark express) | Durée (+3 min) | Haute | fait |
| 2 | **Tableau « fiabilité des données »** | L'utilisateur sait ce qui est mesuré, déclaré ou déduit | Élevée | Aucun | Haute | fait |
| 3 | **Liste des réglages écartés et pourquoi** | Désamorce les « astuces » dangereuses vues sur Internet | Élevée | Aucun | Haute | fait |
| 4 | **Reprise d'une optimisation interrompue** | Pas d'état incohérent après une coupure | Élevée | Aucun | Haute | fait |
| 5 | **Préférence GPU par jeu** (`DirectX\UserGpuPreferences`, réglage officiel de Windows) | Évite qu'un portable lance un jeu sur l'iGPU | Élevée, HKCU sans droits | Faible | Haute | proposé |
| 6 | **Calibration des estimations par les captures réelles** | Les estimations deviennent des mesures | Élevée | Aucun | Haute | fait |
| 7 | **Vérification après redémarrage** | Confirme que Windows a conservé les réglages | Élevée | Aucun | Moyenne | fait |
| 8 | **Alerte « réglage Windows réinitialisé par une mise à jour »** au lancement | Les mises à jour de fonctionnalités annulent certains réglages | Élevée (relecture du journal) | Faible | Moyenne | partiel (bouton « Vérifier et réparer maintenant ») |
| 9 | **Rapport de comparaison partageable avant/après** (HTML) | Utile pour un ami ou un forum | Élevée | Données personnelles → anonymisation | Moyenne | proposé |
| 10 | **Mode « session de jeu »** : surveillance légère pendant une partie puis résumé (frametimes, températures, bridage) | Comprendre une baisse ponctuelle | Moyenne (ETW + capteurs) | Surcharge si mal dosé | Moyenne | proposé |
| 11 | **Contrôle d'alimentation portable** (secteur/batterie, overlay) avec conseil adapté | Les portables perdent beaucoup sur batterie | Élevée | Autonomie | Moyenne | partiel (overlay) |
| 12 | **Vérification de la configuration d'écran par jeu** (VRR actif, fréquence max, HDR) | Erreur fréquente : jeu à 60 Hz sur écran 165 Hz | Moyenne | Aucun | Moyenne | partiel (fréquence max) |

## Écartés (gadgets ou risque)

- « Booster de RAM » / vidage de la mémoire en attente : contre-productif.
- Overclocking CPU depuis Windows sans interface officielle : nécessiterait un pilote noyau.
- Téléchargement automatique de pilotes depuis des sites agrégateurs : sources non fiables.
- Tableau de bord surchargé de compteurs en temps réel : coût CPU de l'application elle-même.
