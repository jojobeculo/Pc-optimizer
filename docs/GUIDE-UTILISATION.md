# Guide d'utilisation de FrameCore

## 1. Premier lancement

1. Ouvrez **FrameCore** depuis le menu Démarrer. L'application démarre **sans droits administrateur** : l'analyse ne
   modifie rien.
2. Le diagnostic se lance automatiquement (page **Accueil**). Il lit le matériel, Windows, les pilotes, les
   programmes au démarrage et produit des **constats** classés :
   - 🔴 *critique* : problème qui pénalise fortement (RAM en simple canal, pilote graphique de base, disque presque plein…) ;
   - 🟠 *avertissement* : point à corriger ;
   - 🔵 *suggestion* : amélioration possible.
3. Chaque constat explique la cause et propose une action (ouvrir un réglage, une page officielle, un guide BIOS…).

> Les valeurs que FrameCore ne peut pas lire (capteur absent, droits nécessaires, API indisponible) sont affichées
> « indéterminé » : aucune valeur n'est inventée.

## 2. Optimiser en mode simple

Page **Optimisation** :

1. Choisissez un profil :
   - **Légère** — réglages confirmés, sans effet secondaire notable (idéal pour un PC partagé ou un portable) ;
   - **Équilibrée** — ajoute des réglages probables à risque faible ;
   - **Maximale** — ajoute des réglages plus poussés et, si vous les cochez, des réglages expérimentaux.
2. Cliquez sur **Préparer le plan**. FrameCore affiche, pour cette machine :
   - les modifications proposées (cochées), avec la valeur actuelle et la valeur cible ;
   - les réglages **déjà en place** ;
   - les réglages **exclus** car incompatibles (avec la raison : portable, Ryzen X3D bi-CCD, build Windows, etc.) ;
   - les réglages qui demandent une **validation spécifique** (jamais cochés par défaut) ;
   - les actions qui nécessitent votre intervention (BIOS, pilote).
3. Décochez ce que vous ne voulez pas, puis **Appliquer la sélection**.
4. Windows demande l'autorisation administrateur (UAC) une seule fois : le processus élevé reste disponible jusqu'à la fermeture de FrameCore, sans nouvelle demande.
5. Un point de restauration Windows est créé avant l'application (option désactivable dans *Paramètres*).
6. Le résultat indique pour chaque réglage s'il a été **appliqué et vérifié**, et si un redémarrage est nécessaire.

## 3. Mode Pro

La page **Mode Pro** donne accès à chaque réglage, par catégorie (Windows, processeur, carte graphique, mémoire,
stockage, réseau, périphériques, confidentialité, services…). Pour chacun :

- nom, description et **mécanisme technique** ;
- **valeur actuelle** lue sur la machine et **valeur recommandée** ;
- impact attendu, régressions possibles, niveau de confiance, risque, compatibilité, redémarrage ;
- boutons **Appliquer** / **Restaurer**.

Sections supplémentaires :

- **Démarrage et extensions** : programmes lancés au démarrage (registre, dossiers, tâches, services tiers) avec
  leur classification ; la désactivation est réversible et n'efface rien.
- **Surveillance** : graphiques temps réel CPU, GPU, RAM, températures (si les capteurs sont disponibles).
- **Profils personnalisés** : enregistrez l'ensemble des optimisations actives, exportez/importez-le sur un autre PC
  (les réglages incompatibles avec ce PC seront ignorés).
- **Sauvegardes** : voir le [guide de restauration](GUIDE-RESTAURATION.md).

## 4. Matériel

La page **Matériel** détaille chaque composant : modèle exact, génération, cœurs/threads, cache, 3D V-Cache, VRAM,
pilote, HAGS, ReBAR, lien PCIe, barrettes RAM (vitesse configurée en MT/s, canaux, référence du kit, XMP/EXPO
probable), carte mère et BIOS, disques (type, santé, usure, température), écrans (mode actuel, fréquence max,
VRR, HDR, EDID), réseau et sécurité (Defender, pare-feu, VBS/HVCI — en lecture seule).

## 5. Pilotes

- Liste des pilotes installés, avec fournisseur, version, date et **signataire**.
- **Vérifier les mises à jour** : interroge Windows Update (catégorie Pilotes) et permet d'installer la sélection.
- **Sources officielles** : liens directs vers les pages des fabricants de vos composants (NVIDIA, AMD, Intel,
  fabricant de la carte mère ou du PC). Aucun site tiers.

## 6. Overclocking

> L'overclocking peut rendre le système instable. FrameCore n'expose **aucun réglage de tension**, n'écrit jamais
> dans le BIOS et revient aux réglages d'usine en cas d'échec d'un test.

- **Carte graphique** (NVIDIA via NVML, AMD via ADLX, si le pilote les fournit) :
  - *Analyser les capacités* : plages réellement autorisées par le pilote ;
  - *Réglage manuel* : offset cœur, offset mémoire, limite de puissance ;
  - *Réglage automatique* : montée par paliers de 15 MHz avec test de stabilité à chaque palier, recul au dernier
    palier stable, contrôle de la bande passante mémoire, validation finale ;
  - *Préréglages AMD* : overclock/undervolt automatiques fournis par le pilote AMD ;
  - *Test de stabilité GPU* (calcul D3D11 vérifié) ; *Réglages d'usine*.
- **Processeur** : test de stabilité (calculs vérifiés + erreurs matérielles WHEA), conseils adaptés (AMD PBO /
  Curve Optimizer, limites Intel, microcode Intel 13e/14e génération) et liens vers les outils officiels. Le
  réglage se fait dans le BIOS ou dans Ryzen Master / Intel XTU.

## 7. Benchmark

1. Lancez votre jeu, placez-vous au début d'une scène reproductible.
2. Page **Benchmark** : choisissez le processus du jeu, la durée et le nombre de passages (3 recommandés).
3. **Démarrer la capture**. Entre deux passages, revenez au même point de départ.
4. La session affiche FPS moyen, 1 % low, 0,1 % low (si ≥ 10 000 images), frametimes P95/P99, saccades,
   et l'analyse des limitations (CPU, GPU, VRAM…).
5. Faites une session **avant** et une **après** optimisation, puis **Comparaison A / B** : FrameCore indique si
   la différence est significative ou dans le bruit de mesure.

Méthodes de capture : ETW intégré (droits administrateur), Intel PresentMon (si vous l'avez installé ; la
signature Intel est vérifiée), ou import d'un CSV PresentMon. Un benchmark synthétique (CPU, mémoire) est aussi
disponible.

## 8. Paramètres

Langue (français / anglais), thème (sombre / clair), point de restauration automatique, capteurs avancés
(LibreHardwareMonitor + pilote PawnIO à installer vous-même), chemin de PresentMon, rapport de diagnostic
(HTML/JSON, anonymisation), vérification des mises à jour (désactivée par défaut), dossier des données et journaux.

## 9. Partager FrameCore

Copiez simplement l'installateur sur un autre PC : il ne contient aucune donnée de votre machine. Le nouveau PC
réalisera sa propre détection. Pour transférer vos réglages, exportez un **profil personnalisé** et importez-le.
