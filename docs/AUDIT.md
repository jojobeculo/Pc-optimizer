# Rapport d'audit — FrameCore 1.0 → 2.0

Méthode : relecture du code et des 15 modules, exécution réelle sur une machine virtuelle Windows jetable
(GitHub Actions, Windows Server 2025 24H2 : tests d'intégration qui appliquent puis restaurent chaque optimisation,
visite automatique de tous les écrans avec captures, erreurs de liaison WPF, mesures de démarrage/mémoire/CPU),
puis correction. Gravité : **critique** (perte de données ou système modifié sans retour possible), **majeure**
(résultat faux ou fonction cassée), **modérée** (information trompeuse, friction importante), **mineure**.

## 1. Constats et corrections

| # | Gravité | Domaine | Constat | Correction |
|---|---|---|---|---|
| 1 | Critique | Restauration | Fichier d'échange personnalisé (`D:\pagefile.sys`) : l'application échouait à moitié puis la restauration ne recréait pas la configuration d'origine (constaté sur Windows réel). | Réglage limité à sa fonction (réparer un fichier d'échange **désactivé**) ; une configuration personnalisée n'est jamais touchée ; restauration recréant les fichiers d'échange d'origine ; vérification de la liste complète. |
| 2 | Critique | Moteur | Rollback : l'action qui échoue n'était pas restaurée alors qu'elle pouvait avoir écrit partiellement. | Le rollback inclut l'action en échec (restauration idempotente). Test de non-régression. |
| 3 | Majeure | Moteur | Aucune relecture après application : une valeur ignorée par Windows ou une stratégie était annoncée « appliquée ». | Relecture systématique ; état *vérifié / non effectif / non vérifiable* par action ; Vérification & Réparation (2 réécritures max puis restauration). |
| 4 | Majeure | Restauration | Hibernation : une valeur de registre absente à l'origine restait créée après restauration. | Restauration de l'absence (suppression) ; réglage « non applicable » si l'hibernation n'est pas configurée. |
| 5 | Majeure | Journal | Opération interrompue : une entrée non encore marquée « appliquée » devenait non restaurable. | Toute entrée dont l'état initial a été capturé reste restaurable ; lots planifiés enregistrés avant toute modification ; reprise sans double application. |
| 6 | Majeure | Alimentation | Réglages processeur absents (VM, certains firmwares) : échec « fichier introuvable ». Plan Performances ultimes : échec si présent mais caché, ou non pris en charge. | Statut « non applicable sur cette machine » ; schémas cachés détectés et jamais supprimés. |
| 7 | Majeure | Plans | Deux plans d'alimentation pouvaient être appliqués dans le même profil. | Champ `conflicts` dans les fiches ; un plan ne contient jamais deux réglages en conflit ; validation du catalogue. |
| 8 | Majeure | Données affichées | Température CPU « 0 °C » affichée quand le capteur renvoie 0 (valeur fictive) ; courbes à 0 % quand l'utilisation GPU est inconnue. | Températures hors 5–125 °C traitées comme inconnues ; trous dans les courbes au lieu de zéros. |
| 9 | Modérée | Détection | « Double canal » affirmé sur une VM ou à partir du seul nombre de barrettes. | Détection des machines virtuelles ; niveau de confiance (mesuré / déclaré / déduit / indisponible) ; tableau « Fiabilité des données ». |
| 10 | Modérée | Windows | Cycle de vie client appliqué à Windows Server (« 24H2 n'est plus pris en charge »). | Windows Server signalé comme hors périmètre de la table client. |
| 11 | Modérée | Localisation | Dates et nombres au format américain dans l'interface française (C# et liaisons WPF). | Culture alignée sur la langue de l'interface, y compris `FrameworkElement.Language`. |
| 12 | Modérée | Pilotes | Pilotes intégrés Microsoft (datés 21/06/2006 par convention) signalés comme « anciens ». | Exclus de l'alerte d'ancienneté. |
| 13 | Modérée | Interface | Fenêtre plus grande que l'écran (1024×768 constaté) : navigation coupée. | Taille limitée à la zone de travail ; maximisation sur petits écrans. |
| 14 | Modérée | Performance de l'app | Diagnostic de 10,6 s, dont 5,6 s pour l'analyse du démarrage (sondage de signature de chaque binaire système). | Analyses en parallèle ; binaires Windows non sondés inutilement. |
| 15 | Modérée | Tests | Les tests unitaires écrivaient dans le vrai `%LOCALAPPDATA%\FrameCore` (journaux « Applied … » simulés). | Racine de données isolée pour les tests. |
| 16 | Modérée | Profils | Les niveaux ne correspondaient pas à la demande (Légère / Équilibrée / Maximale). | Équilibré / Performances / Performance maximale (journaux 1.0 relus). |
| 17 | Mineure | Interface | Valeurs brutes « (absent) », « None » ; boutons A/B actifs sans session. | Libellés humains ; états désactivés. |
| 18 | Mineure | Benchmark | La progression du benchmark pouvait reculer entre deux étapes (double comptage, trouvé par un test). | Corrigé et couvert par un test. |
| 19 | Mineure | Libellés | « % Performance Limit » présenté comme une limitation alors que 100 % = aucune limitation. | Commentaire et libellés corrigés ; drapeaux de limitation lus. |

## 2. Vérifications sans défaut

- **Aucun bouton décoratif** : contrôle automatique de toutes les liaisons `Command` des vues vers des commandes existantes (62/62) et 0 erreur de liaison WPF sur ~70 écrans visités sur Windows réel.
- **Sécurité** : aucune action du catalogue ne vise Defender, le pare-feu, Windows Update, VBS/HVCI, Secure Boot, BitLocker ou les mitigations CPU (test automatique) ; le processus élevé n'accepte que des actions identiques au catalogue.
- **Restauration exacte** : sur Windows réel, chaque optimisation compatible est appliquée, vérifiée puis restaurée, et l'état est comparé à l'état d'origine (test d'intégration).

## 3. Limites connues (non corrigeables ici)

- Le matériel du runner (VM sans GPU, capteurs absents) ne permet pas de tester l'overclocking NVIDIA/AMD, les capteurs LibreHardwareMonitor ni la capture ETW d'un vrai jeu.
- L'exécutable n'est pas signé (aucun certificat fourni) : avertissement SmartScreen possible.
- Les indices GPU/CPU et les points de référence des jeux sont des ordres de grandeur ; ils sont présentés comme tels (plage + confiance) et remplacés par les mesures réelles.
