# Catalogue des optimisations

Généré automatiquement depuis `catalog/tweaks/*.json` (`python3 build/gen-catalog-doc.py`).
**87 optimisations.** La valeur initiale est lue sur chaque machine au moment du diagnostic et enregistrée avant toute modification ; elle apparaît dans FrameCore (Mode Pro) et dans le journal de restauration.

Méthode commune à toutes les fiches : (1) lecture de la valeur actuelle, (2) vérification de la compatibilité, (3) enregistrement de l'état initial, (4) application, (5) relecture, (6) vérification de la prise en compte, (7) journalisation. En cas d'échec, les actions déjà faites sont annulées.

## Synthèse

| Identifiant | Nom | Catégorie | Confiance | Risque | Profils | Peut influencer |
|---|---|---|---|---|---|---|
| `gaming.gamemode` | Activer le Mode Jeu | Windows | Confirmé | Nul | Équilibré, Performances, Performance maximale | FPS, Régularité des images |
| `gaming.dvr.background` | Désactiver l'enregistrement en arrière-plan | Windows | Confirmé | Nul | Équilibré, Performances, Performance maximale | FPS, Régularité des images |
| `gaming.dvr.capture` | Désactiver les captures de la Game Bar | Windows | Probable | Faible | Performances, Performance maximale | FPS, Régularité des images |
| `graphics.windowed` | Optimisations pour les jeux fenêtrés | Windows | Confirmé | Faible | Équilibré, Performances, Performance maximale | Latence d'entrée, Régularité des images |
| `graphics.vrr` | Taux d'actualisation variable (Windows) | Windows | Probable | Faible | Performances, Performance maximale | Régularité des images |
| `graphics.hags` | Planification GPU accélérée matériellement | Carte graphique | Probable | Faible | Performances, Performance maximale | FPS, Latence d'entrée |
| `graphics.mpo.disable` | Désactiver le Multi-Plane Overlay (dépannage) | Carte graphique | Non recommandé | Moyen | Mode Pro uniquement | Régularité des images, Stabilité / réparation |
| `graphics.autohdr` | Auto HDR | Écran | Confirmé | Nul | Mode Pro uniquement | Confort |
| `display.maxRefresh` | Utiliser la fréquence maximale de l'écran | Écran | Confirmé | Faible | Équilibré, Performances, Performance maximale | FPS, Régularité des images, Latence d'entrée |
| `visual.transparency` | Désactiver la transparence | Windows | Probable | Nul | Équilibré, Performances, Performance maximale | Réactivité du système |
| `visual.animations` | Désactiver les animations | Windows | Confirmé | Nul | Équilibré, Performances, Performance maximale | Réactivité du système |
| `visual.menudelay` | Menus plus rapides | Windows | Confirmé | Nul | Performances, Performance maximale | Réactivité du système |
| `content.suggestions` | Désactiver suggestions et astuces | Confidentialité | Confirmé | Nul | Équilibré, Performances, Performance maximale | Confidentialité, Réactivité du système |
| `content.silentapps` | Bloquer l'installation silencieuse d'applications promues | Confidentialité | Confirmé | Nul | Équilibré, Performances, Performance maximale | Confidentialité, Espace disque |
| `widgets.disable` | Désactiver les Widgets | Windows | Probable | Faible | Performances, Performance maximale | Réactivité du système |
| `search.websuggestions` | Désactiver la recherche Web dans Démarrer | Confidentialité | Confirmé | Nul | Performances, Performance maximale | Confidentialité, Réactivité du système |
| `notifications.scoobe` | Désactiver « Terminer la configuration de l'appareil » | Windows | Confirmé | Nul | Équilibré, Performances, Performance maximale | Confort |
| `privacy.backgroundapps` | Empêcher les applications du Store de tourner en arrière-plan | Confidentialité | Probable | Faible | Performances, Performance maximale | Réactivité du système, Autonomie (effet négatif) |
| `privacy.adid` | Désactiver l'identifiant publicitaire | Confidentialité | Confirmé | Nul | Performances, Performance maximale | Confidentialité |
| `privacy.tailored` | Désactiver les expériences personnalisées | Confidentialité | Confirmé | Nul | Performances, Performance maximale | Confidentialité |
| `privacy.telemetry` | Télémétrie au minimum (données requises) | Confidentialité | Confirmé | Nul | Performances, Performance maximale | Confidentialité |
| `privacy.recall` | Désactiver Recall (captures IA) | Confidentialité | Probable | Nul | Performances, Performance maximale | Confidentialité, Espace disque |
| `privacy.activityhistory` | Désactiver l'historique d'activité | Confidentialité | Confirmé | Nul | Performances, Performance maximale | Confidentialité |
| `privacy.inking` | Désactiver la collecte d'écriture manuscrite et de saisie | Confidentialité | Confirmé | Nul | Performances, Performance maximale | Confidentialité |
| `privacy.feedback` | Ne plus demander de commentaires | Confidentialité | Confirmé | Nul | Équilibré, Performances, Performance maximale | Confort, Confidentialité |
| `visual.startup.delay` | Supprimer le délai de lancement des applications au démarrage | Windows | Probable | Faible | Performance maximale | Démarrage |
| `svc.diagtrack` | Service de télémétrie (DiagTrack) désactivé | Services | Probable | Faible | Performance maximale | Confidentialité, Réactivité du système |
| `svc.mapsbroker` | Gestionnaire de cartes téléchargées en manuel | Services | Probable | Nul | Performance maximale | Réactivité du système |
| `svc.trkwks` | Client de suivi des liens distribués en manuel | Services | Probable | Nul | Performance maximale | Réactivité du système |
| `svc.pcasvc` | Assistant Compatibilité des programmes en manuel | Services | Probable | Nul | Performance maximale | Réactivité du système |
| `svc.spooler` | Spouleur d'impression en manuel (sans imprimante) | Services | Probable | Faible | Performance maximale | Réactivité du système |
| `svc.sysmain` | SysMain (Superfetch) en manuel | Services | Expérimental | Faible | Extrême | Réactivité du système |
| `svc.wsearch` | Indexation Windows Search en manuel | Services | Expérimental | Moyen | Extrême | Réactivité du système |
| `task.appraiser` | Tâche Microsoft Compatibility Appraiser | Services | Probable | Faible | Performance maximale | Confidentialité, Réactivité du système |
| `task.ceip` | Tâches du Programme d'amélioration de l'expérience | Services | Probable | Nul | Performance maximale | Confidentialité |
| `task.diskdiag` | Collecte de diagnostics disque | Services | Probable | Nul | Performance maximale | Confidentialité |
| `task.feedback` | Tâches de commentaires Windows | Services | Probable | Nul | Performance maximale | Confidentialité |
| `task.maps` | Mise à jour des cartes hors connexion | Services | Probable | Nul | Performance maximale | Réactivité du système |
| `storage.optimize.task` | Réactiver l'optimisation planifiée des disques | Stockage | Confirmé | Nul | Équilibré, Performances, Performance maximale | Stabilité / réparation |
| `power.repair.boost` | Boost processeur actif (secteur) | Processeur | Confirmé | Nul | Équilibré, Performances, Performance maximale | FPS, Régularité des images |
| `power.repair.maxstate` | État maximal du processeur à 100 % (secteur) | Processeur | Confirmé | Nul | Équilibré, Performances, Performance maximale | FPS, Régularité des images |
| `power.plan.high` | Plan « Performances élevées » | Processeur | Probable | Faible | Performances, Performance maximale | FPS, Régularité des images, Latence d'entrée |
| `power.plan.ultimate` | Plan « Performances optimales » | Processeur | Expérimental | Moyen | Performance maximale | FPS, Régularité des images, Latence d'entrée |
| `power.overlay.best` | Mode d'alimentation « Meilleures performances » | Processeur | Probable | Faible | Performances, Performance maximale | FPS, Régularité des images |
| `power.usb.suspend` | Désactiver la suspension sélective USB (secteur) | Périphériques | Probable | Nul | Performances, Performance maximale | Latence d'entrée, Stabilité / réparation |
| `power.pcie.aspm` | Gestion d'état du lien PCIe désactivée (secteur) | Processeur | Probable | Nul | Performances, Performance maximale | Latence d'entrée, Stabilité / réparation |
| `power.disk.timeout` | Ne jamais arrêter les disques durs (secteur) | Stockage | Probable | Nul | Performances, Performance maximale | Réactivité du système |
| `power.minstate` | État minimal du processeur à 100 % | Processeur | Expérimental | Faible | Extrême | Latence d'entrée |
| `power.coreparking` | Désactiver le parcage des cœurs | Processeur | Expérimental | Moyen | Extrême | Régularité des images |
| `power.epp` | Préférence énergétique : performance maximale | Processeur | Expérimental | Faible | Mode Pro uniquement | FPS, Latence d'entrée |
| `power.fastboot` | Désactiver le démarrage rapide | Windows | Probable | Nul | Extrême | Stabilité / réparation, Démarrage |
| `net.wifi.powersave` | Wi-Fi : performances maximales sur secteur | Réseau | Probable | Nul | Performances, Performance maximale | Latence réseau |
| `power.throttling.off` | Désactiver le Power Throttling (EcoQoS) | Processeur | Expérimental | Faible | Performance maximale | Régularité des images, Autonomie (effet négatif) |
| `sched.priosep.38` | Win32PrioritySeparation = 0x26 (placebo) | Processeur | Non recommandé | Faible | Mode Pro uniquement | Réactivité du système |
| `mmcss.responsiveness` | MMCSS SystemResponsiveness = 10 | Processeur | Expérimental | Faible | Mode Pro uniquement | Régularité des images |
| `mmcss.networkthrottling` | Désactiver la limitation réseau MMCSS | Réseau | Expérimental | Faible | Mode Pro uniquement | Latence réseau |
| `mmcss.gamestask` | Priorités MMCSS de la tâche « Games » | Processeur | Expérimental | Faible | Mode Pro uniquement | Régularité des images |
| `timer.global` | Résolution de timer globale | Processeur | Expérimental | Faible | Extrême | Latence d'entrée |
| `bcd.platformclock.remove` | Retirer le forçage de HPET (useplatformclock) | Processeur | Confirmé | Faible | Équilibré, Performances, Performance maximale | Régularité des images, Stabilité / réparation |
| `bcd.dynamictick.remove` | Retirer le forçage disabledynamictick | Processeur | Expérimental | Faible | Mode Pro uniquement | Latence d'entrée |
| `input.mouse.accel` | Désactiver « Améliorer la précision du pointeur » | Périphériques | Confirmé | Nul | Performances, Performance maximale | Latence d'entrée, Confort |
| `input.stickykeys` | Désactiver le raccourci des touches rémanentes | Périphériques | Confirmé | Nul | Performances, Performance maximale | Confort |
| `net.repair.autotuning` | Auto-réglage de la fenêtre TCP sur « normal » | Réseau | Confirmé | Nul | Équilibré, Performances, Performance maximale | Latence réseau |
| `net.eee` | Désactiver l'Ethernet économe en énergie (EEE) | Réseau | Probable | Faible | Performances, Performance maximale | Latence réseau, Stabilité / réparation |
| `net.intmod` | Désactiver la modération des interruptions | Réseau | Expérimental | Moyen | Mode Pro uniquement | Latence réseau |
| `storage.trim` | TRIM activé | Stockage | Confirmé | Nul | Équilibré, Performances, Performance maximale | Stabilité / réparation |
| `storage.retrim` | ReTrim des SSD | Stockage | Confirmé | Nul | Équilibré, Performances, Performance maximale | Stabilité / réparation |
| `cleanup.temp.user` | Nettoyer les fichiers temporaires (utilisateur) | Stockage | Confirmé | Nul | Équilibré, Performances, Performance maximale | Espace disque |
| `cleanup.temp.system` | Nettoyer les fichiers temporaires (système) | Stockage | Confirmé | Nul | Performances, Performance maximale | Espace disque |
| `storage.pagefile.auto` | Fichier d'échange géré par le système | Mémoire | Confirmé | Nul | Performances, Performance maximale | Stabilité / réparation |
| `storage.hibernate.off` | Désactiver la veille prolongée | Stockage | Probable | Faible | Extrême | Espace disque |
| `security.vmp` | Désactiver la Plateforme de machine virtuelle | Sécurité | Probable | Moyen | Performance maximale | FPS |
| `input.accessibility.shortcuts` | Désactiver les raccourcis Touches filtres et Touches bascule | Périphériques | Confirmé | Nul | Performances, Performance maximale | Confort |
| `security.hvci.off` | Désactiver l'intégrité de la mémoire (HVCI) | Sécurité | Probable | Élevé | Extrême | FPS, Régularité des images |
| `defender.scan.cpulimit` | Limiter les analyses planifiées de Defender à 20 % du processeur | Sécurité | Probable | Faible | Extrême | Régularité des images, Réactivité du système |
| `defender.gamefolders.exclude` | Exclure les dossiers de jeux de l'analyse en temps réel de Defender | Sécurité | Probable | Moyen | Extrême | Régularité des images |
| `network.deliveryoptimization.nopeering` | Ne plus partager les mises à jour avec d'autres PC | Réseau | Confirmé | Faible | Performance maximale | Latence réseau, Réactivité du système |
| `edge.background.off` | Empêcher Microsoft Edge de tourner en arrière-plan | Windows | Confirmé | Faible | Extrême | Réactivité du système |
| `svc.dmwappush` | Service de routage des messages WAP (télémétrie) en manuel | Services | Probable | Nul | Performance maximale, Extrême | Réactivité du système |
| `svc.wersvc` | Rapport d'erreurs Windows en manuel | Services | Probable | Nul | Extrême | Réactivité du système |
| `svc.lfsvc` | Service de géolocalisation en manuel | Services | Probable | Nul | Extrême | Réactivité du système |
| `task.winsat` | Évaluation des performances WinSAT | Services | Probable | Nul | Performance maximale, Extrême | Réactivité du système |
| `task.powerdiag` | Diagnostic d'efficacité énergétique | Services | Probable | Nul | Performance maximale, Extrême | Réactivité du système |
| `task.autochkproxy` | Proxy Autochk (programme d'amélioration) | Services | Probable | Nul | Performance maximale, Extrême | Confidentialité |
| `task.werqueue` | File des rapports d'erreurs | Services | Probable | Nul | Extrême | Confidentialité, Réactivité du système |
| `power.boost.aggressive` | Mode de boost du processeur : agressif | Power | Probable | Faible | Performances, Performance maximale, Extrême | Régularité des images, Réactivité du système |
| `net.flowcontrol` | Contrôle de flux Ethernet désactivé | Réseau | Expérimental | Faible | Extrême | Latence réseau |

## Fiches détaillées

### Activer le Mode Jeu (`gaming.gamemode`)

- **Catégorie** : Windows — **Objectif** : Donner la priorité au jeu au premier plan et bloquer les interruptions de Windows Update pendant une partie.
- **Description** : Le Mode Jeu donne la priorité au jeu au premier plan et empêche Windows Update d'installer des pilotes ou d'afficher des notifications de redémarrage pendant une partie.
- **Impact attendu** : Évite des interruptions et une partie de l'activité de fond pendant le jeu ; gain de FPS faible ou nul sur une machine déjà inactive.
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` = `1` (DWord)
  - Registre `HKCU\Software\Microsoft\GameBar\AllowAutoGameMode` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://support.microsoft.com/en-us/windows/options-to-optimize-gaming-performance-in-windows-11-a255f612-2949-4373-a566-ff6f3f474613

### Désactiver l'enregistrement en arrière-plan (`gaming.dvr.background`)

- **Catégorie** : Windows — **Objectif** : Supprimer l'enregistrement vidéo permanent en arrière-plan, qui consomme des ressources d'encodage.
- **Description** : « Enregistrer ce qui vient de se passer » encode en permanence la vidéo du jeu (encodeur GPU, disque).
- **Impact attendu** : Supprime un encodage vidéo permanent s'il était activé.
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : La capture rétroactive de la Game Bar n'est plus disponible.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver les captures de la Game Bar (`gaming.dvr.capture`)

- **Catégorie** : Windows — **Objectif** : Désactiver la capture de jeu Xbox Game Bar quand elle n'est pas utilisée.
- **Description** : Désactive la capture vidéo/screenshots de la Xbox Game Bar (Win+Alt+R).
- **Impact attendu** : Retire des hooks de capture ; gain de FPS non démontré.
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : Plus de capture via la Game Bar. Ne pas utiliser sur Ryzen X3D bi-CCD.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `cpu.isDualCcdX3D isFalse`
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` = `0` (DWord)
  - Registre `HKCU\System\GameConfigStore\GameDVR_Enabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Optimisations pour les jeux fenêtrés (`graphics.windowed`)

- **Catégorie** : Windows — **Objectif** : Utiliser le modèle de présentation moderne (flip) pour les jeux DirectX 10/11 en fenêtre ou sans bordure.
- **Description** : Utilise le modèle de présentation « flip » pour les jeux DirectX 10/11 en fenêtré ou sans bordure.
- **Impact attendu** : Latence réduite en fenêtré/sans bordure ; rend possibles la VRR et Auto HDR dans ces modes (documentation Microsoft).
- **Peut influencer** : Latence d'entrée, Régularité des images
- **Régressions possibles** : Rares jeux anciens incompatibles (désactivable par jeu dans Paramètres > Graphismes).
- **Confiance** : Confirmé — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : `os.build >= 22621`
- **Conflits** : aucun
- **Application** :
  - Réglage DirectX `SwapEffectUpgradeEnable` = `1`
- **Vérification** : Relecture des réglages graphiques DirectX
- **Restauration** : Réécriture de la chaîne DirectX d'origine
- **Sources** : https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952

### Taux d'actualisation variable (Windows) (`graphics.vrr`)

- **Catégorie** : Windows — **Objectif** : Autoriser le taux de rafraîchissement variable pour les jeux qui ne le gèrent pas eux-mêmes.
- **Description** : Active la VRR de Windows pour les jeux DirectX 11 plein écran qui ne la gèrent pas nativement.
- **Impact attendu** : Supprime le tearing sans la latence de la V-Sync classique, si l'écran est compatible.
- **Peut influencer** : Régularité des images
- **Régressions possibles** : Scintillement possible sur certaines dalles à faible fréquence.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `display.vrrCapable isTrue`
- **Conflits** : aucun
- **Application** :
  - Réglage DirectX `VRROptimizeEnable` = `1`
- **Vérification** : Relecture des réglages graphiques DirectX
- **Restauration** : Réécriture de la chaîne DirectX d'origine

### Planification GPU accélérée matériellement (`graphics.hags`)

- **Catégorie** : Carte graphique — **Objectif** : Confier la planification de la mémoire vidéo à la carte graphique (requis par certaines fonctions comme la génération d'images).
- **Description** : Le GPU gère lui-même sa file de travail (WDDM 2.7+).
- **Impact attendu** : Réduit un peu l'overhead CPU de planification ; requis par DLSS Frame Generation. Effet variable selon les jeux.
- **Peut influencer** : FPS, Latence d'entrée
- **Régressions possibles** : Incompatibilités ponctuelles (VR, logiciels de capture anciens).
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `gpu.hagsSupported isTrue`
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` = `2` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver le Multi-Plane Overlay (dépannage) (`graphics.mpo.disable`)

- **Catégorie** : Carte graphique — **Objectif** : Contourner des scintillements ou saccades liés aux plans de superposition sur certains pilotes.
- **Description** : Désactive les plans de superposition matériels du DWM. Contournement de scintillements signalés par certains pilotes.
- **Impact attendu** : Aucun gain de FPS : uniquement pour le dépannage.
- **Peut influencer** : Régularité des images, Stabilité / réparation
- **Régressions possibles** : Peut augmenter la latence et la consommation en fenêtré.
- **Confiance** : Non recommandé — **Risque** : Moyen — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode` = `5` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Auto HDR (`graphics.autohdr`)

- **Catégorie** : Écran — **Objectif** : Désactiver la conversion HDR automatique des jeux SDR (choix visuel, pas de performance).
- **Description** : Convertit automatiquement les jeux SDR (DX11/12) en HDR.
- **Impact attendu** : Préférence visuelle : aucun gain de performance (léger coût GPU).
- **Peut influencer** : Confort — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Rendu parfois trop lumineux selon les jeux.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : `display.hdrCapable isTrue`
- **Conflits** : aucun
- **Application** :
  - Réglage DirectX `AutoHDREnable` = `1`
- **Vérification** : Relecture des réglages graphiques DirectX
- **Restauration** : Réécriture de la chaîne DirectX d'origine

### Utiliser la fréquence maximale de l'écran (`display.maxRefresh`)

- **Catégorie** : Écran — **Objectif** : Utiliser la fréquence maximale de l'écran au lieu d'un mode 60 Hz laissé par défaut.
- **Description** : Règle chaque écran sur la fréquence la plus élevée proposée par Windows à sa résolution actuelle (testé avant application).
- **Impact attendu** : Un écran 144 Hz réglé à 60 Hz n'affiche que 60 images/s : gain de fluidité majeur.
- **Peut influencer** : FPS, Régularité des images, Latence d'entrée
- **Régressions possibles** : Retour automatique à l'ancien mode si l'affichage n'est pas confirmé dans les 15 secondes.
- **Confiance** : Confirmé — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `display.maxRefresh`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Restauration propre à l'opération

### Désactiver la transparence (`visual.transparency`)

- **Catégorie** : Windows — **Objectif** : Réduire le travail de composition du bureau (effets de transparence).
- **Description** : Désactive l'effet acrylique/mica de l'interface Windows.
- **Impact attendu** : Améliore la réactivité du bureau sur GPU intégré modeste ; aucun effet en jeu plein écran.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Esthétique uniquement.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver les animations (`visual.animations`)

- **Catégorie** : Windows — **Objectif** : Rendre l'interface plus réactive en supprimant les animations de fenêtres.
- **Description** : Désactive les effets d'animation de Windows (Accessibilité > Effets visuels).
- **Impact attendu** : Interface plus réactive (suppression des délais d'animation). Pas d'effet sur les FPS.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Esthétique uniquement.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - SystemParametersInfo `clientAreaAnimation` = `0`
- **Vérification** : Relecture du paramètre système (SystemParametersInfo)
- **Restauration** : Valeur système d'origine rétablie

### Menus plus rapides (`visual.menudelay`)

- **Catégorie** : Windows — **Objectif** : Afficher les menus sans délai.
- **Description** : Réduit le délai d'ouverture des sous-menus de 400 à 200 ms.
- **Impact attendu** : Réactivité perçue ; pas d'effet sur les FPS.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - SystemParametersInfo `menuShowDelay` = `200`
- **Vérification** : Relecture du paramètre système (SystemParametersInfo)
- **Restauration** : Valeur système d'origine rétablie

### Désactiver suggestions et astuces (`content.suggestions`)

- **Catégorie** : Confidentialité — **Objectif** : Supprimer les suggestions et contenus promotionnels de Windows.
- **Description** : Désactive les contenus suggérés de Démarrer, des Paramètres et les astuces Windows.
- **Impact attendu** : Confort et moins de contenus promotionnels ; pas d'effet sur les FPS.
- **Peut influencer** : Confidentialité, Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-338388Enabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-338389Enabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-353694Enabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-353696Enabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SoftLandingEnabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SystemPaneSuggestionsEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Bloquer l'installation silencieuse d'applications promues (`content.silentapps`)

- **Catégorie** : Confidentialité — **Objectif** : Empêcher l'installation silencieuse d'applications suggérées.
- **Description** : Empêche Windows d'installer automatiquement des applications sponsorisées.
- **Impact attendu** : Évite des téléchargements et installations en arrière-plan.
- **Peut influencer** : Confidentialité, Espace disque — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SilentInstalledAppsEnabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\PreInstalledAppsEnabled` = `0` (DWord)
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\OemPreInstalledAppsEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver les Widgets (`widgets.disable`)

- **Catégorie** : Windows — **Objectif** : Arrêter le panneau Widgets et ses processus en arrière-plan.
- **Description** : Stratégie officielle qui désactive le tableau Widgets (processus WebView2).
- **Impact attendu** : Libère de la mémoire et de l'activité de fond ; pas de gain de FPS démontré.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Plus de Widgets ni de météo dans la barre des tâches.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Fermeture de session
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `os.isWindows11 isTrue`
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver la recherche Web dans Démarrer (`search.websuggestions`)

- **Catégorie** : Confidentialité — **Objectif** : Limiter la recherche Windows aux résultats locaux.
- **Description** : Stratégie officielle qui retire les suggestions Bing de la recherche Windows.
- **Impact attendu** : Recherche locale plus rapide et confidentialité ; pas d'effet sur les FPS.
- **Peut influencer** : Confidentialité, Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Plus de résultats Web dans Démarrer.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Redémarrage de l'Explorateur
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver « Terminer la configuration de l'appareil » (`notifications.scoobe`)

- **Catégorie** : Windows — **Objectif** : Supprimer les écrans « Terminons la configuration » après les mises à jour.
- **Description** : Supprime les écrans de suggestion de configuration après les mises à jour.
- **Impact attendu** : Confort.
- **Peut influencer** : Confort — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement\ScoobeSystemSettingEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Empêcher les applications du Store de tourner en arrière-plan (`privacy.backgroundapps`)

- **Catégorie** : Confidentialité — **Objectif** : Empêcher les applications du Store de tourner en arrière-plan.
- **Description** : Désactive globalement l'exécution en arrière-plan des applications UWP/Store.
- **Impact attendu** : Moins d'activité CPU/réseau de fond.
- **Peut influencer** : Réactivité du système, Autonomie (effet négatif) — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Les notifications de certaines applications Store (Courrier, Mobile connecté, nouvelle version de Teams) peuvent ne plus arriver.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\GlobalUserDisabled` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver l'identifiant publicitaire (`privacy.adid`)

- **Catégorie** : Confidentialité — **Objectif** : Désactiver l'identifiant publicitaire.
- **Description** : Les applications ne peuvent plus utiliser l'identifiant publicitaire.
- **Impact attendu** : Confidentialité ; pas d'effet sur les performances.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver les expériences personnalisées (`privacy.tailored`)

- **Catégorie** : Confidentialité — **Objectif** : Désactiver les expériences personnalisées à partir des données de diagnostic.
- **Description** : Windows n'utilise plus les données de diagnostic pour personnaliser astuces et publicités.
- **Impact attendu** : Confidentialité.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy\TailoredExperiencesWithDiagnosticDataEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Télémétrie au minimum (données requises) (`privacy.telemetry`)

- **Catégorie** : Confidentialité — **Objectif** : Réduire les données de diagnostic au niveau requis.
- **Description** : Stratégie officielle « AllowTelemetry = 1 » (données de diagnostic requises). La valeur 0 n'est effective qu'en Entreprise/Éducation.
- **Impact attendu** : Confidentialité ; aucun gain de performance démontré.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Certaines fonctions du programme Insider ou de diagnostic sont limitées.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver Recall (captures IA) (`privacy.recall`)

- **Catégorie** : Confidentialité — **Objectif** : Désactiver l'enregistrement des instantanés Recall.
- **Description** : Stratégie officielle qui désactive l'enregistrement de captures par Recall (PC Copilot+).
- **Impact attendu** : Évite l'analyse et l'écriture de captures en arrière-plan.
- **Peut influencer** : Confidentialité, Espace disque — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Plus de recherche Recall.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Fermeture de session
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `os.build >= 26100`
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Désactiver l'historique d'activité (`privacy.activityhistory`)

- **Catégorie** : Confidentialité — **Objectif** : Ne pas conserver ni transmettre l'historique d'activité.
- **Description** : Empêche Windows de publier et d'envoyer l'historique des applications et fichiers ouverts (stratégie PublishUserActivities / UploadUserActivities).
- **Impact attendu** : Confidentialité ; aucun effet sur les FPS.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : La reprise d'activités sur un autre appareil n'est plus proposée.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Fermeture de session
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\PublishUserActivities` = `0` (DWord)
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\UploadUserActivities` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services

### Désactiver la collecte d'écriture manuscrite et de saisie (`privacy.inking`)

- **Catégorie** : Confidentialité — **Objectif** : Ne pas collecter d'échantillons d'écriture et de frappe.
- **Description** : Désactive la personnalisation de la saisie qui collecte des exemples d'écriture et de frappe (InputPersonalization).
- **Impact attendu** : Confidentialité ; aucun effet sur les FPS.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : La reconnaissance d'écriture et les suggestions de saisie s'adaptent moins à vous.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\InputPersonalization\RestrictImplicitTextCollection` = `1` (DWord)
  - Registre `HKCU\Software\Microsoft\InputPersonalization\RestrictImplicitInkCollection` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services

### Ne plus demander de commentaires (`privacy.feedback`)

- **Catégorie** : Confidentialité — **Objectif** : Supprimer les fenêtres de sondage pendant l'utilisation.
- **Description** : Règle la fréquence des demandes de commentaires de Windows sur « Jamais » (Siuf NumberOfSIUFInPeriod = 0).
- **Impact attendu** : Moins d'interruptions ; aucun effet sur les FPS.
- **Peut influencer** : Confort, Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Siuf\Rules\NumberOfSIUFInPeriod` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services

### Supprimer le délai de lancement des applications au démarrage (`visual.startup.delay`)

- **Catégorie** : Windows — **Objectif** : Rendre le bureau utilisable plus vite après l'ouverture de session.
- **Description** : Explorer attend quelques secondes avant de lancer les applications de démarrage (Serialize\StartupDelayInMSec). La valeur 0 les lance immédiatement.
- **Impact attendu** : Les applications de démarrage sont prêtes plus tôt ; aucun effet en jeu.
- **Peut influencer** : Démarrage — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Avec beaucoup d'applications au démarrage, l'ouverture de session peut sembler plus chargée pendant quelques secondes.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Fermeture de session
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize\StartupDelayInMSec` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/slow-startup-of-apps

### Service de télémétrie (DiagTrack) désactivé (`svc.diagtrack`)

- **Catégorie** : Services — **Objectif** : Arrêter le service de télémétrie utilisateur connecté.
- **Description** : « Expériences des utilisateurs connectés et télémétrie » collecte et envoie les données de diagnostic.
- **Impact attendu** : Supprime une activité disque/réseau ponctuelle ; gain de FPS non démontré.
- **Peut influencer** : Confidentialité, Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Programme Windows Insider et certains diagnostics indisponibles.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `DiagTrack` → démarrage `Disabled`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Gestionnaire de cartes téléchargées en manuel (`svc.mapsbroker`)

- **Catégorie** : Services — **Objectif** : Arrêter le gestionnaire de cartes téléchargées si l'application Cartes n'est pas utilisée.
- **Description** : Ne démarre plus automatiquement ; démarre si une application Cartes en a besoin.
- **Impact attendu** : Négligeable (démarrage un peu plus léger).
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `MapsBroker` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Client de suivi des liens distribués en manuel (`svc.trkwks`)

- **Catégorie** : Services — **Objectif** : Arrêter le suivi des liens distribués (utile seulement en réseau d'entreprise NTFS).
- **Description** : Suivi des fichiers liés sur des volumes NTFS en réseau.
- **Impact attendu** : Négligeable.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Raccourcis réseau déplacés moins bien résolus.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `TrkWks` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Assistant Compatibilité des programmes en manuel (`svc.pcasvc`)

- **Catégorie** : Services — **Objectif** : Arrêter l'assistant de compatibilité des programmes.
- **Description** : Surveille les programmes pour proposer des correctifs de compatibilité.
- **Impact attendu** : Négligeable.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Moins de propositions de compatibilité pour les vieux logiciels.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `PcaSvc` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Spouleur d'impression en manuel (sans imprimante) (`svc.spooler`)

- **Catégorie** : Services — **Objectif** : Arrêter le spouleur d'impression sur un PC sans imprimante.
- **Description** : Proposé uniquement si aucune imprimante physique n'est installée.
- **Impact attendu** : Négligeable en FPS ; réduit la surface d'attaque (failles PrintNightmare).
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Impression et « Imprimer en PDF » indisponibles jusqu'à restauration.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : `printers.physicalCount == 0`
- **Conflits** : aucun
- **Application** :
  - Service `Spooler` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### SysMain (Superfetch) en manuel (`svc.sysmain`)

- **Catégorie** : Services — **Objectif** : Arrêter le préchargement en mémoire (SysMain) — effet incertain, souvent nul sur SSD.
- **Description** : SysMain précharge en mémoire les applications fréquemment utilisées.
- **Impact attendu** : Aucun gain de FPS démontré ; peut ralentir le lancement des applications et des jeux.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Lancements plus lents, surtout sur HDD.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `SysMain` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Indexation Windows Search en manuel (`svc.wsearch`)

- **Catégorie** : Services — **Objectif** : Arrêter l'indexation de recherche (la recherche de fichiers devient plus lente).
- **Description** : Arrête l'indexation des fichiers. Windows réduit déjà l'indexation pendant l'activité de l'utilisateur.
- **Impact attendu** : Réduit l'activité disque après de gros changements de fichiers ; gain en jeu non démontré.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Recherche Démarrer/Explorateur/Outlook beaucoup plus lente.
- **Confiance** : Expérimental — **Risque** : Moyen — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `WSearch` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Tâche Microsoft Compatibility Appraiser (`task.appraiser`)

- **Catégorie** : Services — **Objectif** : Désactiver l'évaluation de compatibilité périodique.
- **Description** : Évaluation de compatibilité (CompatTelRunner.exe) parfois très gourmande en CPU.
- **Impact attendu** : Supprime des pics CPU/disque imprévisibles.
- **Peut influencer** : Confidentialité, Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Windows peut évaluer moins bien la compatibilité avant une mise à jour de fonctionnalités (la tâche peut être réactivée par Windows).
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Tâches du Programme d'amélioration de l'expérience (`task.ceip`)

- **Catégorie** : Services — **Objectif** : Désactiver les tâches du programme d'amélioration de l'expérience utilisateur.
- **Description** : Collecte de données d'usage (Consolidator, UsbCeip).
- **Impact attendu** : Négligeable.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Customer Experience Improvement Program\Consolidator` → désactivée
  - Tâche planifiée `\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Collecte de diagnostics disque (`task.diskdiag`)

- **Catégorie** : Services — **Objectif** : Désactiver la collecte de diagnostics disque envoyée à Microsoft.
- **Description** : Envoie des informations de diagnostic disque à Microsoft.
- **Impact attendu** : Négligeable.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Tâches de commentaires Windows (`task.feedback`)

- **Catégorie** : Services — **Objectif** : Désactiver les tâches de collecte de commentaires.
- **Description** : Sollicitations de commentaires (DmClient).
- **Impact attendu** : Négligeable.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Feedback\Siuf\DmClient` → désactivée
  - Tâche planifiée `\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Mise à jour des cartes hors connexion (`task.maps`)

- **Catégorie** : Services — **Objectif** : Désactiver la mise à jour automatique des cartes hors ligne.
- **Description** : Télécharge les mises à jour des cartes hors connexion.
- **Impact attendu** : Négligeable.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Maps\MapsUpdateTask` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Réactiver l'optimisation planifiée des disques (`storage.optimize.task`)

- **Catégorie** : Stockage — **Objectif** : Garantir que l'optimisation hebdomadaire (TRIM sur SSD) reste active.
- **Description** : Réparation : la tâche ScheduledDefrag exécute ReTrim sur SSD et la défragmentation sur HDD. Certains « optimiseurs » la désactivent à tort.
- **Impact attendu** : Maintient les performances des SSD dans le temps.
- **Peut influencer** : Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Defrag\ScheduledDefrag` → activée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Boost processeur actif (secteur) (`power.repair.boost`)

- **Catégorie** : Processeur — **Objectif** : Rétablir le Turbo/Boost du processeur s'il a été désactivé.
- **Description** : Réparation : remet le mode de boost du processeur sur « Agressif » (valeur par défaut du plan Équilibré) s'il a été désactivé.
- **Impact attendu** : Un boost désactivé bloque le CPU à sa fréquence de base : perte de performance importante.
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `be337238-0d82-4146-a960-4f3749d470c7` (sous-groupe `54533251-82be-4824-96c1-47eeb4c3c3a1`) secteur=2 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### État maximal du processeur à 100 % (secteur) (`power.repair.maxstate`)

- **Catégorie** : Processeur — **Objectif** : Rétablir l'état maximal du processeur à 100 % s'il a été bridé.
- **Description** : Réparation : une valeur inférieure à 100 % plafonne la fréquence du processeur.
- **Impact attendu** : Restaure la fréquence maximale.
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `bc5038f7-23e0-4960-96da-33abaf5935ec` (sous-groupe `54533251-82be-4824-96c1-47eeb4c3c3a1`) secteur=100 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### Plan « Performances élevées » (`power.plan.high`)

- **Catégorie** : Processeur — **Objectif** : Réduire la latence de montée en fréquence avec le plan Performances élevées.
- **Description** : État minimal du processeur à 100 % et pas de parcage des cœurs.
- **Impact attendu** : Montée en fréquence plus rapide, parfois de meilleurs 1 % low ; gain variable selon la machine (à mesurer).
- **Peut influencer** : FPS, Régularité des images, Latence d'entrée
- **Régressions possibles** : Consommation et chaleur au repos plus élevées.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `system.isDesktop isTrue` ET `cpu.isDualCcdX3D isFalse` ET `cpu.vendor != amd` ET `system.modernStandby isFalse` ET `power.highPerformanceAvailable isTrue`
- **Conflits** : `power.overlay.best`, `power.plan.ultimate`
- **Application** :
  - Activer le plan `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`
- **Vérification** : Relecture du plan d'alimentation actif
- **Restauration** : Ancien plan réactivé ; plan créé par FrameCore supprimé

### Plan « Performances optimales » (`power.plan.ultimate`)

- **Catégorie** : Processeur — **Objectif** : Supprimer presque toutes les économies d'énergie (plan Performances ultimes).
- **Description** : Plan caché de Windows destiné aux stations de travail : supprime des micro-latences de gestion de l'énergie. Remplace le plan actif (ne pas combiner avec « Performances élevées »).
- **Impact attendu** : Aucun gain en jeu démontré de façon reproductible.
- **Peut influencer** : FPS, Régularité des images, Latence d'entrée
- **Régressions possibles** : Consommation et chaleur élevées en permanence.
- **Confiance** : Expérimental — **Risque** : Moyen — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : `system.isDesktop isTrue` ET `cpu.isDualCcdX3D isFalse` ET `system.modernStandby isFalse`
- **Conflits** : `power.overlay.best`, `power.plan.high`
- **Application** :
  - Activer le plan `e9a42b02-d5df-448d-aa00-03f14749eb61` (créé par duplication de `e9a42b02-d5df-448d-aa00-03f14749eb61` si absent)
- **Vérification** : Relecture du plan d'alimentation actif
- **Restauration** : Ancien plan réactivé ; plan créé par FrameCore supprimé

### Mode d'alimentation « Meilleures performances » (`power.overlay.best`)

- **Catégorie** : Processeur — **Objectif** : Choisir le mode d'alimentation « Performances optimales » sur portable.
- **Description** : Équivalent du curseur Paramètres > Alimentation (mode actuel : secteur ou batterie). API Windows non documentée.
- **Impact attendu** : Performances soutenues plus élevées sur portable.
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : Autonomie et chaleur.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : au moins une de : `system.isLaptop isTrue` ; `system.modernStandby isTrue`
- **Conflits** : `power.plan.high`, `power.plan.ultimate`
- **Application** :
  - Mode d'alimentation `None`
- **Vérification** : Relecture du mode d'alimentation
- **Restauration** : Mode d'alimentation d'origine rétabli

### Désactiver la suspension sélective USB (secteur) (`power.usb.suspend`)

- **Catégorie** : Périphériques — **Objectif** : Éviter la mise en veille sélective des périphériques USB sur secteur.
- **Description** : Empêche Windows de mettre en veille les périphériques USB inactifs.
- **Impact attendu** : Évite les micro-coupures ou délais de réveil de souris, casques et DAC USB.
- **Peut influencer** : Latence d'entrée, Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Légère hausse de consommation.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `48e6b7a6-50f5-4782-a5d4-53bb8f07e226` (sous-groupe `2a737441-1930-4402-8d77-b2bebba308a3`) secteur=0 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### Gestion d'état du lien PCIe désactivée (secteur) (`power.pcie.aspm`)

- **Catégorie** : Processeur — **Objectif** : Désactiver l'économie d'énergie du lien PCI Express sur secteur.
- **Description** : Désactive l'ASPM côté Windows (le BIOS peut aussi l'imposer).
- **Impact attendu** : Évite la latence de sortie de veille L1 des liens GPU/NVMe.
- **Peut influencer** : Latence d'entrée, Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Consommation au repos un peu plus élevée.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `system.isDesktop isTrue`
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `ee12f906-d277-404b-b6da-e5fa1a576df5` (sous-groupe `501a4d13-42af-4429-9fd1-a8218c268e20`) secteur=0 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### Ne jamais arrêter les disques durs (secteur) (`power.disk.timeout`)

- **Catégorie** : Stockage — **Objectif** : Empêcher l'arrêt des disques durs qui provoque des attentes au chargement.
- **Description** : Les HDD ne s'arrêtent plus après inactivité.
- **Impact attendu** : Évite des saccades de plusieurs secondes au réveil d'un HDD contenant des jeux.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Usure et consommation légèrement supérieures.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `storage.hasHdd isTrue`
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `6738e2c4-e8a5-4a42-b16a-e040e769756e` (sous-groupe `0012ee47-9041-4b5d-9b77-535fba8b1442`) secteur=0 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### État minimal du processeur à 100 % (`power.minstate`)

- **Catégorie** : Processeur — **Objectif** : Maintenir une fréquence minimale plus haute (consommation au repos accrue).
- **Description** : Empêche le CPU de baisser sa fréquence au repos.
- **Impact attendu** : Peut réduire le boost (chaleur, limites de puissance) : effet souvent nul ou négatif.
- **Peut influencer** : Latence d'entrée — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Chaleur et consommation.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `893dee8e-2bef-41e0-89c6-b55d0929964c` (sous-groupe `54533251-82be-4824-96c1-47eeb4c3c3a1`) secteur=100 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### Désactiver le parcage des cœurs (`power.coreparking`)

- **Catégorie** : Processeur — **Objectif** : Garder tous les cœurs éveillés (désactiver le stationnement).
- **Description** : Garde tous les cœurs actifs.
- **Impact attendu** : Résultats variables ; Windows gère déjà finement le parcage.
- **Peut influencer** : Régularité des images
- **Régressions possibles** : Interdit sur CPU hybrides Intel et Ryzen X3D bi-CCD (parcage volontaire).
- **Confiance** : Expérimental — **Risque** : Moyen — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : `cpu.isDualCcdX3D isFalse` ET `cpu.isHybrid isFalse`
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `0cc5b647-c1df-4637-891a-dec35c318583` (sous-groupe `54533251-82be-4824-96c1-47eeb4c3c3a1`) secteur=100 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### Préférence énergétique : performance maximale (`power.epp`)

- **Catégorie** : Processeur — **Objectif** : Orienter la préférence énergétique du processeur vers la performance.
- **Description** : EPP = 0 sur secteur (CPU avec HWP/CPPC).
- **Impact attendu** : Montée en fréquence plus agressive ; effet variable.
- **Peut influencer** : FPS, Latence d'entrée
- **Régressions possibles** : Chaleur.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `36687f9e-e3a5-4dbf-b1dc-15eb381c6863` (sous-groupe `54533251-82be-4824-96c1-47eeb4c3c3a1`) secteur=0 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies

### Désactiver le démarrage rapide (`power.fastboot`)

- **Catégorie** : Windows — **Objectif** : Désactiver le démarrage rapide pour un vrai redémarrage à chaque arrêt.
- **Description** : Le démarrage rapide restaure un noyau en veille prolongée au lieu d'un démarrage à froid.
- **Impact attendu** : Noyau et pilotes réinitialisés à chaque démarrage (stabilité, mises à jour de pilotes).
- **Peut influencer** : Stabilité / réparation, Démarrage — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Démarrage un peu plus long.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Wi-Fi : performances maximales sur secteur (`net.wifi.powersave`)

- **Catégorie** : Réseau — **Objectif** : Éviter que la carte Wi-Fi se mette en veille entre deux paquets pendant une partie en ligne.
- **Description** : Règle le mode d'économie d'énergie de la carte sans fil sur « Performances maximales » lorsque le PC est branché (plan d'alimentation actif).
- **Impact attendu** : Peut réduire les pics de latence Wi-Fi ; n'augmente pas les FPS.
- **Peut influencer** : Latence réseau — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Consommation légèrement plus élevée sur secteur.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `net.hasWifi == True`
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `12bbebe6-58d6-4636-95bb-3217ef867c1a` (sous-groupe `19cbb8fa-5279-450e-9fac-8a3d5fedd0c1`) secteur=0 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies
- **Sources** : https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/wireless-adapter-settings-power-saving-mode

### Désactiver le Power Throttling (EcoQoS) (`power.throttling.off`)

- **Catégorie** : Processeur — **Objectif** : Éviter le bridage des logiciels de capture ou d'encodage en arrière-plan.
- **Description** : Windows bride les processus en arrière-plan (EcoQoS), qui peuvent être placés sur les cœurs efficaces. Le désactiver laisse les logiciels de fond (enregistrement, streaming) à pleine vitesse.
- **Impact attendu** : Utile seulement si un logiciel en arrière-plan (OBS, encodage) ralentit pendant le jeu ; aucun gain pour le jeu au premier plan.
- **Peut influencer** : Régularité des images, Autonomie (effet négatif)
- **Régressions possibles** : Consommation et températures plus élevées, autonomie réduite sur portable.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling\PowerThrottlingOff` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service

### Win32PrioritySeparation = 0x26 (placebo) (`sched.priosep.38`)

- **Catégorie** : Processeur — **Objectif** : Modifier la priorité du premier plan (placebo démontré : valeur équivalente au défaut).
- **Description** : Réglage très répandu. Décodage : intervalles courts, variables, boost 3:1 du premier plan — exactement ce que signifie la valeur par défaut 2 sur un Windows client.
- **Impact attendu** : Aucun : comportement identique à la valeur par défaut.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Non recommandé — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation` = `38` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### MMCSS SystemResponsiveness = 10 (`mmcss.responsiveness`)

- **Catégorie** : Processeur — **Objectif** : Réserver moins de temps CPU aux tâches de fond multimédia.
- **Description** : Part de CPU réservée aux tâches de faible priorité quand des threads MMCSS sont actifs. Selon Microsoft, 0 est ramené à 20 (défaut) : 10 est le minimum effectif.
- **Impact attendu** : Ne concerne que les threads enregistrés auprès de MMCSS (audio, rares jeux).
- **Peut influencer** : Régularité des images
- **Régressions possibles** : Aucune connue.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness` = `10` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service

### Désactiver la limitation réseau MMCSS (`mmcss.networkthrottling`)

- **Catégorie** : Réseau — **Objectif** : Supprimer la limitation réseau appliquée pendant la lecture multimédia.
- **Description** : NetworkThrottlingIndex = 0xFFFFFFFF : supprime la limitation du traitement réseau pendant la lecture multimédia.
- **Impact attendu** : Effet en jeu non démontré.
- **Peut influencer** : Latence réseau — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Légère hausse CPU réseau pendant la lecture audio.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex` = `0xFFFFFFFF` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service

### Priorités MMCSS de la tâche « Games » (`mmcss.gamestask`)

- **Catégorie** : Processeur — **Objectif** : Augmenter la priorité MMCSS de la tâche « Games ».
- **Description** : Priorité 6, catégorie High, priorité E/S High pour les threads MMCSS « Games ».
- **Impact attendu** : Ne concerne que les jeux qui appellent AvSetMmThreadCharacteristics("Games"), ce qui est rare.
- **Peut influencer** : Régularité des images
- **Régressions possibles** : Aucune connue.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Priority` = `6` (DWord)
  - Registre `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Scheduling Category` = `High` (String)
  - Registre `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\SFIO Priority` = `High` (String)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service

### Résolution de timer globale (`timer.global`)

- **Catégorie** : Processeur — **Objectif** : Rétablir la résolution de minuterie globale d'avant Windows 10 2004.
- **Description** : Depuis Windows 10 2004, la résolution du timer demandée par un programme ne s'applique plus aux autres ; ce réglage restaure l'ancien comportement global.
- **Impact attendu** : Utile seulement pour des programmes qui comptent sur la requête d'un autre ; les jeux demandent leur propre résolution.
- **Peut influencer** : Latence d'entrée — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Consommation plus élevée (le CPU entre moins en veille).
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\GlobalTimerResolutionRequests` = `1` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod

### Retirer le forçage de HPET (useplatformclock) (`bcd.platformclock.remove`)

- **Catégorie** : Processeur — **Objectif** : Supprimer un forçage de l'horloge HPET qui augmente le coût des mesures de temps.
- **Description** : Réparation : si un outil a forcé l'horloge plateforme (HPET) comme source de QueryPerformanceCounter, Windows revient à son choix automatique (TSC).
- **Impact attendu** : Selon Microsoft, une horloge plateforme impose un appel système et ~0,8–1 µs par lecture de QPC : forcé, HPET peut réduire les performances.
- **Peut influencer** : Régularité des images, Stabilité / réparation
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Supprimer l'option BCD forcée `useplatformclock`
- **Vérification** : Relecture de la configuration de démarrage (bcdedit)
- **Restauration** : Option de démarrage d'origine réécrite (redémarrage requis)
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps

### Retirer le forçage disabledynamictick (`bcd.dynamictick.remove`)

- **Catégorie** : Processeur — **Objectif** : Supprimer un réglage forcé du tick dynamique.
- **Description** : Rétablit le « dynamic tick » par défaut si un outil l'a désactivé.
- **Impact attendu** : Le forçage n'a pas d'effet de latence démontré et augmente la consommation.
- **Peut influencer** : Latence d'entrée — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Supprimer l'option BCD forcée `disabledynamictick`
- **Vérification** : Relecture de la configuration de démarrage (bcdedit)
- **Restauration** : Option de démarrage d'origine réécrite (redémarrage requis)

### Désactiver « Améliorer la précision du pointeur » (`input.mouse.accel`)

- **Catégorie** : Périphériques — **Objectif** : Rendre le mouvement de la souris proportionnel (sans accélération).
- **Description** : Supprime l'accélération du pointeur Windows.
- **Impact attendu** : Visée plus constante au bureau et dans les jeux sans « raw input ». Aucun effet sur les FPS ; sans effet dans les jeux utilisant le raw input.
- **Peut influencer** : Latence d'entrée, Confort — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Préférence : certains utilisateurs préfèrent l'accélération.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - SystemParametersInfo `mouseAcceleration` = `0,0,0`
- **Vérification** : Relecture du paramètre système (SystemParametersInfo)
- **Restauration** : Valeur système d'origine rétablie

### Désactiver le raccourci des touches rémanentes (`input.stickykeys`)

- **Catégorie** : Périphériques — **Objectif** : Empêcher la fenêtre des touches rémanentes après 5 appuis sur Maj.
- **Description** : Empêche la fenêtre des touches rémanentes après 5 appuis sur Maj.
- **Impact attendu** : Confort en jeu (pas d'interruption).
- **Peut influencer** : Confort — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Fermeture de session
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Control Panel\Accessibility\StickyKeys\Flags` = `506` (String)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### Auto-réglage de la fenêtre TCP sur « normal » (`net.repair.autotuning`)

- **Catégorie** : Réseau — **Objectif** : Rétablir le réglage automatique de la fenêtre TCP s'il a été désactivé.
- **Description** : Réparation : désactiver l'auto-tuning (conseil répandu) réduit fortement le débit sans améliorer le ping.
- **Impact attendu** : Restaure le débit des téléchargements.
- **Peut influencer** : Latence réseau — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `net.autotuning.normal`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Restauration propre à l'opération

### Désactiver l'Ethernet économe en énergie (EEE) (`net.eee`)

- **Catégorie** : Réseau — **Objectif** : Désactiver l'Ethernet économe en énergie (micro-coupures de lien).
- **Description** : Mot-clé normalisé *EEE des cartes Ethernet physiques.
- **Impact attendu** : Supprime les micro-latences de sortie de veille du lien (LPI) ; gain faible.
- **Peut influencer** : Latence réseau, Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Légère hausse de consommation ; coupure réseau de quelques secondes à l'application.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Redémarrage de la carte réseau
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : `net.hasEthernet isTrue`
- **Conflits** : aucun
- **Application** :
  - Carte réseau physique : propriété `*EEE` = `0`
- **Vérification** : Relecture de la propriété avancée de la carte réseau
- **Restauration** : Valeur d'origine de la propriété rétablie

### Désactiver la modération des interruptions (`net.intmod`)

- **Catégorie** : Réseau — **Objectif** : Désactiver la modération des interruptions (moins de latence, plus de charge CPU).
- **Description** : Chaque paquet génère une interruption.
- **Impact attendu** : Latence légèrement réduite au prix d'une charge CPU plus élevée ; résultats variables.
- **Peut influencer** : Latence réseau — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Charge CPU plus élevée à fort débit.
- **Confiance** : Expérimental — **Risque** : Moyen — **Redémarrage** : Redémarrage de la carte réseau
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Mode Pro uniquement
- **Compatibilité** : `net.hasEthernet isTrue`
- **Conflits** : aucun
- **Application** :
  - Carte réseau physique : propriété `*InterruptModeration` = `0`
- **Vérification** : Relecture de la propriété avancée de la carte réseau
- **Restauration** : Valeur d'origine de la propriété rétablie

### TRIM activé (`storage.trim`)

- **Catégorie** : Stockage — **Objectif** : Garantir que TRIM est activé pour les SSD.
- **Description** : Réparation : TRIM informe le SSD des blocs libérés.
- **Impact attendu** : Indispensable pour maintenir les performances d'écriture d'un SSD.
- **Peut influencer** : Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : `storage.hasSsd isTrue`
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\DisableDeleteNotification` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas

### ReTrim des SSD (`storage.retrim`)

- **Catégorie** : Stockage — **Objectif** : Envoyer immédiatement les commandes TRIM aux SSD.
- **Description** : Exécute l'optimisation Windows (defrag.exe /L) sur les volumes SSD. Jamais de défragmentation classique sur SSD.
- **Impact attendu** : Maintenance ; utile si l'optimisation planifiée n'a pas tourné depuis longtemps.
- **Peut influencer** : Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Opération ponctuelle (rien à restaurer)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : `storage.hasSsd isTrue`
- **Conflits** : aucun
- **Application** :
  - Opération `storage.retrim`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Opération ponctuelle : rien à restaurer.

### Nettoyer les fichiers temporaires (utilisateur) (`cleanup.temp.user`)

- **Catégorie** : Stockage — **Objectif** : Libérer l'espace occupé par les fichiers temporaires de l'utilisateur.
- **Description** : Supprime les fichiers de %TEMP%, les vidages de plantage et rapports d'erreurs de plus de 24 h. Fichiers en cours d'utilisation ignorés ; caches de shaders jamais supprimés (cela provoquerait des saccades).
- **Impact attendu** : Libère de l'espace disque ; pas d'effet sur les FPS.
- **Peut influencer** : Espace disque — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Opération sans retour arrière (fichiers temporaires).
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Opération ponctuelle (rien à restaurer)
- **Profils** : Équilibré, Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `cleanup.temp.user`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Opération ponctuelle : rien à restaurer.

### Nettoyer les fichiers temporaires (système) (`cleanup.temp.system`)

- **Catégorie** : Stockage — **Objectif** : Libérer l'espace occupé par les fichiers temporaires de Windows.
- **Description** : Windows\Temp et rapports d'erreurs système archivés de plus de 24 h.
- **Impact attendu** : Libère de l'espace disque.
- **Peut influencer** : Espace disque — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Opération sans retour arrière.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Opération ponctuelle (rien à restaurer)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `cleanup.temp.system`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Opération ponctuelle : rien à restaurer.

### Fichier d'échange géré par le système (`storage.pagefile.auto`)

- **Catégorie** : Mémoire — **Objectif** : Rétablir un fichier d'échange géré par Windows s'il a été supprimé.
- **Description** : Réparation : réactive un fichier d'échange désactivé. Une configuration personnalisée existante est respectée.
- **Impact attendu** : Sans fichier d'échange, la mémoire engagée est plafonnée par la RAM : plantages de jeux possibles.
- **Peut influencer** : Stabilité / réparation — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `storage.pagefile.auto`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Restauration propre à l'opération

### Désactiver la veille prolongée (`storage.hibernate.off`)

- **Catégorie** : Stockage — **Objectif** : Libérer l'espace du fichier d'hibernation sur un PC fixe.
- **Description** : Supprime hiberfil.sys (environ 40 % de la RAM).
- **Impact attendu** : Libère de l'espace sur le disque système.
- **Peut influencer** : Espace disque — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Plus de veille prolongée ni de démarrage rapide.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : `system.isDesktop isTrue`
- **Conflits** : aucun
- **Application** :
  - Opération `storage.hibernate.off`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Restauration propre à l'opération

### Désactiver la Plateforme de machine virtuelle (`security.vmp`)

- **Catégorie** : Sécurité — **Objectif** : Désactiver la plateforme de machine virtuelle si aucune fonction ne l'utilise (WSL2, Android).
- **Description** : Fonctionnalité facultative de Windows. Microsoft indique qu'elle peut réduire les performances en jeu sur certaines configurations. Ce n'est pas une protection de sécurité.
- **Impact attendu** : Gain variable selon la machine (à mesurer avec le Benchmark).
- **Peut influencer** : FPS
- **Régressions possibles** : WSL 2, Windows Subsystem for Android, Windows Sandbox et certaines machines virtuelles ne fonctionnent plus.
- **Confiance** : Probable — **Risque** : Moyen — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale — validation spécifique requise
- **Compatibilité** : `security.vmpEnabled isTrue`
- **Conflits** : aucun
- **Application** :
  - Fonctionnalité Windows `VirtualMachinePlatform` → désactivée
- **Vérification** : Relecture de l'état de la fonctionnalité (DISM)
- **Restauration** : État d'origine de la fonctionnalité rétabli (redémarrage possible)
- **Sources** : https://support.microsoft.com/en-us/windows/options-to-optimize-gaming-performance-in-windows-11-a255f612-2949-4373-a566-ff6f3f474613

### Désactiver les raccourcis Touches filtres et Touches bascule (`input.accessibility.shortcuts`)

- **Catégorie** : Périphériques — **Objectif** : Supprimer des interruptions accidentelles pendant le jeu.
- **Description** : Empêche l'ouverture des boîtes de dialogue d'accessibilité lorsqu'on maintient Maj droite 8 secondes ou Verr. num 5 secondes (indicateurs Keyboard Response et ToggleKeys).
- **Impact attendu** : Évite qu'une fenêtre sorte le jeu du plein écran ; aucun effet sur les FPS.
- **Peut influencer** : Confort — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Les fonctions restent activables dans Paramètres > Accessibilité.
- **Confiance** : Confirmé — **Risque** : Nul — **Redémarrage** : Fermeture de session
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKCU\Control Panel\Accessibility\Keyboard Response\Flags` = `122` (String)
  - Registre `HKCU\Control Panel\Accessibility\ToggleKeys\Flags` = `58` (String)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-filterkeys

### Désactiver l'intégrité de la mémoire (HVCI) (`security.hvci.off`)

- **Catégorie** : Sécurité — **Objectif** : Supprimer le coût de la virtualisation du contrôle des pilotes, mesurable dans les jeux limités par le processeur.
- **Description** : Désactive l'isolation du noyau « Intégrité de la mémoire » (sécurité basée sur la virtualisation des pilotes). Microsoft indique que les joueurs qui privilégient les performances peuvent la désactiver.
- **Impact attendu** : Gain variable selon la machine, surtout dans les jeux limités par le processeur (à mesurer avec le Benchmark et une capture en jeu).
- **Peut influencer** : FPS, Régularité des images
- **Régressions possibles** : Protection réduite contre les pilotes malveillants ; Windows Sécurité affiche un avertissement ; certains anti-triche ou logiciels d'entreprise peuvent exiger cette protection (restaurez-la alors depuis Restauration).
- **Confiance** : Probable — **Risque** : Élevé — **Redémarrage** : Redémarrage requis
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême — validation spécifique requise
- **Compatibilité** : `security.hvci isTrue`
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://support.microsoft.com/en-us/windows/options-to-optimize-gaming-performance-in-windows-11-a255f612-2949-4373-a566-ff6f3f474613

### Limiter les analyses planifiées de Defender à 20 % du processeur (`defender.scan.cpulimit`)

- **Catégorie** : Sécurité — **Objectif** : Éviter qu'une analyse en arrière-plan prenne la moitié du processeur pendant une partie.
- **Description** : Une analyse planifiée de Microsoft Defender peut démarrer pendant une partie si le PC n'a pas été inactif à l'heure prévue. Son utilisation du processeur est plafonnée à 20 % au lieu de 50 %. La protection en temps réel n'est pas modifiée.
- **Impact attendu** : Moins de chutes de FPS si une analyse démarre pendant le jeu ; aucun effet le reste du temps.
- **Peut influencer** : Régularité des images, Réactivité du système
- **Régressions possibles** : Les analyses planifiées durent plus longtemps.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême — validation spécifique requise
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `defender.scanCpuLimit`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Restauration propre à l'opération
- **Sources** : https://learn.microsoft.com/en-us/defender-endpoint/configure-advanced-scan-types-microsoft-defender-antivirus

### Exclure les dossiers de jeux de l'analyse en temps réel de Defender (`defender.gamefolders.exclude`)

- **Catégorie** : Sécurité — **Objectif** : Moins d'analyse des fichiers quand un jeu charge ou diffuse ses ressources.
- **Description** : Ajoute à Microsoft Defender une exclusion pour les dossiers de bibliothèque des lanceurs installés (Steam, Epic Games, Riot, Xbox, EA, Ubisoft). Le reste du PC reste analysé en temps réel.
- **Impact attendu** : Chargements et micro-saccades de chargement réduits dans certains jeux ; effet faible sur les FPS moyens.
- **Peut influencer** : Régularité des images
- **Régressions possibles** : Un fichier malveillant placé dans ces dossiers (mod, triche, programme téléchargé) ne serait plus analysé à l'ouverture : installez uniquement des jeux et mods de sources officielles. La restauration retire exactement les dossiers ajoutés.
- **Confiance** : Probable — **Risque** : Moyen — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême — validation spécifique requise
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Opération `defender.gameFolderExclusions`
- **Vérification** : Contrôle propre à l'opération
- **Restauration** : Restauration propre à l'opération
- **Sources** : https://learn.microsoft.com/en-us/defender-endpoint/configure-exclusions-microsoft-defender-antivirus

### Ne plus partager les mises à jour avec d'autres PC (`network.deliveryoptimization.nopeering`)

- **Catégorie** : Réseau — **Objectif** : Supprimer l'envoi de données de mise à jour en arrière-plan (bande passante montante et disque).
- **Description** : Stratégie officielle de l'Optimisation de la distribution : Windows télécharge ses mises à jour uniquement depuis les serveurs Microsoft et n'en envoie plus à d'autres PC. Windows Update n'est pas désactivé.
- **Impact attendu** : Moins de trafic montant et d'activité disque en arrière-plan ; utile en jeu en ligne sur une connexion modeste.
- **Peut influencer** : Latence réseau, Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Les mises à jour ne profitent plus des autres PC du réseau local (téléchargement un peu plus long sur plusieurs PC).
- **Confiance** : Confirmé — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference

### Empêcher Microsoft Edge de tourner en arrière-plan (`edge.background.off`)

- **Catégorie** : Windows — **Objectif** : Libérer la mémoire et le processeur occupés par Edge quand vous ne l'utilisez pas.
- **Description** : Stratégies officielles d'Edge : pas de « démarrage rapide » à l'ouverture de session et pas d'exécution en arrière-plan une fois Edge fermé.
- **Impact attendu** : Quelques processus et quelques centaines de Mo de mémoire en moins en arrière-plan.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Edge s'ouvre un peu plus lentement ; ses pages de paramètres indiquent « géré par votre organisation » (effet normal d'une stratégie).
- **Confiance** : Confirmé — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Edge\StartupBoostEnabled` = `0` (DWord)
  - Registre `HKLM\SOFTWARE\Policies\Microsoft\Edge\BackgroundModeEnabled` = `0` (DWord)
- **Vérification** : Relecture de la valeur de registre
- **Restauration** : Réécriture de la valeur d'origine, ou suppression si elle n'existait pas
- **Sources** : https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies

### Service de routage des messages WAP (télémétrie) en manuel (`svc.dmwappush`)

- **Catégorie** : Services — **Objectif** : Un service de télémétrie de moins au démarrage.
- **Description** : Service utilisé par la télémétrie pour recevoir des messages ; démarre seulement si nécessaire.
- **Impact attendu** : Faible : un service de moins qui démarre et se réveille en arrière-plan.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale, Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `dmwappushservice` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Rapport d'erreurs Windows en manuel (`svc.wersvc`)

- **Catégorie** : Services — **Objectif** : Moins d'activité de fond ; les rapports restent possibles.
- **Description** : Le service de rapport d'erreurs ne démarre que lorsqu'un programme plante.
- **Impact attendu** : Faible : un service de moins qui démarre et se réveille en arrière-plan.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `WerSvc` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Service de géolocalisation en manuel (`svc.lfsvc`)

- **Catégorie** : Services — **Objectif** : Un service de moins pour un PC fixe de jeu.
- **Description** : La géolocalisation ne démarre que si une application la demande.
- **Impact attendu** : Faible : un service de moins qui démarre et se réveille en arrière-plan.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Applications de localisation (météo, cartes) plus lentes à trouver la position.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Service `lfsvc` → démarrage `Manual`
- **Vérification** : Relecture du type de démarrage du service
- **Restauration** : Type de démarrage d'origine rétabli

### Évaluation des performances WinSAT (`task.winsat`)

- **Catégorie** : Services — **Objectif** : Éviter qu'un test de performance se lance pendant une partie.
- **Description** : Ancien test de performance de Windows lancé en maintenance ; son résultat n'est plus utilisé.
- **Impact attendu** : Négligeable à faible : une tâche de fond de moins pendant les parties.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale, Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Maintenance\WinSAT` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Diagnostic d'efficacité énergétique (`task.powerdiag`)

- **Catégorie** : Services — **Objectif** : Une analyse de fond de moins.
- **Description** : Analyse périodique de la consommation pour des rapports.
- **Impact attendu** : Négligeable à faible : une tâche de fond de moins pendant les parties.
- **Peut influencer** : Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale, Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Proxy Autochk (programme d'amélioration) (`task.autochkproxy`)

- **Catégorie** : Services — **Objectif** : Moins de collecte de données.
- **Description** : Collecte des données pour le programme d'amélioration de l'expérience utilisateur.
- **Impact attendu** : Négligeable à faible : une tâche de fond de moins pendant les parties.
- **Peut influencer** : Confidentialité — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performance maximale, Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Autochk\Proxy` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### File des rapports d'erreurs (`task.werqueue`)

- **Catégorie** : Services — **Objectif** : Pas d'envoi de rapports en arrière-plan pendant les parties.
- **Description** : Envoie périodiquement les rapports d'erreurs en attente.
- **Impact attendu** : Négligeable à faible : une tâche de fond de moins pendant les parties.
- **Peut influencer** : Confidentialité, Réactivité du système — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Aucune connue.
- **Confiance** : Probable — **Risque** : Nul — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême
- **Compatibilité** : toutes les machines Windows 10/11 x64
- **Conflits** : aucun
- **Application** :
  - Tâche planifiée `\Microsoft\Windows\Windows Error Reporting\QueueReporting` → désactivée
- **Vérification** : Relecture de l'état de la tâche planifiée
- **Restauration** : État d'origine de la tâche rétabli

### Mode de boost du processeur : agressif (`power.boost.aggressive`)

- **Catégorie** : Power — **Objectif** : Laisser le processeur monter en fréquence sans délai.
- **Description** : Le processeur monte en fréquence dès qu'une charge apparaît, au lieu d'attendre (plan d'alimentation actif, sur secteur).
- **Impact attendu** : Réactivité et images un peu plus régulières quand la charge varie.
- **Peut influencer** : Régularité des images, Réactivité du système
- **Régressions possibles** : Consommation et chaleur un peu plus élevées.
- **Confiance** : Probable — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Performances, Performance maximale, Extrême
- **Compatibilité** : `system.isDesktop isTrue`
- **Conflits** : aucun
- **Application** :
  - Plan d'alimentation : paramètre `be337238-0d82-4146-a960-4f3749d470c7` (sous-groupe `54533251-82be-4824-96c1-47b60b740d00`) secteur=2 batterie=None
- **Vérification** : Relecture du paramètre du plan d'alimentation
- **Restauration** : Valeurs secteur/batterie d'origine rétablies
- **Sources** : https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode

### Contrôle de flux Ethernet désactivé (`net.flowcontrol`)

- **Catégorie** : Réseau — **Objectif** : Éviter les pauses d'émission de la carte réseau.
- **Description** : Empêche la carte réseau de mettre en pause l'envoi quand l'équipement en face le demande.
- **Impact attendu** : Peut réduire des micro-pics de latence sur certaines cartes ; effet variable.
- **Peut influencer** : Latence réseau — *aucune raison technique de modifier les FPS*
- **Régressions possibles** : Sur un réseau saturé, quelques pertes de paquets possibles.
- **Confiance** : Expérimental — **Risque** : Faible — **Redémarrage** : Aucun redémarrage
- **Persistance** : Permanent (survit au redémarrage)
- **Profils** : Extrême — validation spécifique requise
- **Compatibilité** : `net.hasEthernet isTrue`
- **Conflits** : aucun
- **Application** :
  - Carte réseau physique : propriété `*FlowControl` = `0`
- **Vérification** : Relecture de la propriété avancée de la carte réseau
- **Restauration** : Valeur d'origine de la propriété rétablie

## Réglages écartés

Astuces répandues que FrameCore n'applique pas.

| Réglage | Ce qu'on en dit | Ce qu'il en est | Verdict |
|---|---|---|---|
| Désactiver l'algorithme de Nagle (TcpAckFrequency / TCPNoDelay) | Réduirait le ping en jeu. | Les jeux modernes utilisent UDP ou activent eux-mêmes TCP_NODELAY sur leurs sockets. La clé système ne change rien au ping vers le serveur ; elle peut augmenter le nombre d'accusés de réception. | Mythe |
| Changer de DNS pour gagner des FPS ou du ping | Un DNS « gaming » améliorerait les performances. | Le DNS ne sert qu'à résoudre les noms au moment de la connexion. Il n'influence ni les FPS ni la latence pendant la partie. | Mythe |
| Nettoyeurs de RAM (vidage de la liste d'attente) | Libérer la RAM accélérerait le PC. | La mémoire « en attente » est un cache réutilisable instantanément. La vider force Windows à relire les données depuis le disque : cela crée des saccades au lieu d'en supprimer. | Contre-productif |
| LargeSystemCache = 1 | Améliorerait les performances mémoire. | Réglage prévu pour les serveurs de fichiers : il favorise le cache système au détriment des applications. Contre-productif pour un PC de jeu. | Contre-productif |
| DisablePagingExecutive = 1 | Garder le noyau en RAM accélérerait Windows. | Aucun gain mesurable avec 16 Go ou plus : le noyau actif n'est de toute façon pas paginé en pratique. | Mythe |
| Désactiver la compression mémoire | La compression coûterait des performances. | La compression n'intervient que sous pression mémoire ; elle évite alors des écritures dans le fichier d'échange, bien plus lentes. La désactiver aggrave les saccades quand la RAM manque. | Contre-productif |
| Supprimer le fichier d'échange | Avec beaucoup de RAM, il serait inutile. | Certains jeux et pilotes réservent de la mémoire « engagée » qui doit être adossée au fichier d'échange : sans lui, plantages « mémoire insuffisante » possibles, et pas de vidage mémoire en cas d'écran bleu. | Contre-productif |
| Forcer HPET (bcdedit useplatformclock true) | Améliorerait la précision et la fluidité. | Forcer HPET rend chaque lecture d'horloge beaucoup plus coûteuse et peut baisser les FPS dans les jeux qui mesurent souvent le temps. FrameCore propose au contraire de supprimer ce forçage. | Contre-productif |
| Win32PrioritySeparation = 0x26 / 0x28 | Donnerait plus de temps CPU au jeu. | 0x26 équivaut au comportement par défaut (2) de Windows client ; les autres valeurs ne montrent pas de gain reproductible dans les mesures publiées. | Sans effet |
| Désactiver globalement les optimisations plein écran | Réduirait la latence. | Sur Windows 11, le modèle de présentation flip offre une latence équivalente au plein écran exclusif tout en gardant les superpositions. Le réglage global peut dégrader HDR, VRR et le changement de fenêtre. À ajuster seulement par jeu si un problème est constaté. | Mythe |
| Désactiver en masse les services en démarrage manuel | Moins de services = plus de performances. | Un service en démarrage manuel ne consomme rien tant qu'il n'est pas sollicité. Le désactiver ne fait rien gagner et casse les fonctions qui en ont besoin (Bluetooth, impression, Xbox, VPN…). | Contre-productif |
| SvcHostSplitThresholdInKB | Regrouper les services économiserait des ressources. | Le gain mémoire est minime et un plantage d'un service entraîne alors tout son groupe. | Mythe |
| Désactiver l'optimisation des lecteurs sur SSD | L'optimisation userait le SSD. | Sur SSD, Windows n'effectue pas de défragmentation classique : la tâche envoie surtout des commandes TRIM, utiles à la durée de vie et aux performances. FrameCore la garde active. | Contre-productif |
| Vider régulièrement le cache de shaders | Libérerait de l'espace et réglerait les saccades. | Le cache évite de recompiler les shaders : le vider provoque justement des saccades au prochain lancement. À faire uniquement après un problème identifié de pilote. | Contre-productif |
| IRQ8Priority et priorités d'IRQ | Prioriserait l'horloge ou la carte graphique. | Ces valeurs n'ont aucun effet sur les versions NT de Windows : le noyau ne les lit pas. | Sans effet |
| Désactiver VBS / l'intégrité de la mémoire (HVCI) sur un PC ordinaire | Augmenterait les FPS. | Un gain de quelques pourcents est réel dans certains jeux limités par le processeur, mais cela retire une protection importante contre les pilotes malveillants. Jamais proposé dans les niveaux Équilibré, Performances et Performance maximale ; seulement au niveau Extrême (PC qui sert uniquement à jouer), après avertissement, avec consentement et restauration possible à tout moment. VBS lui-même n'est jamais désactivé. | Compromis de sécurité |
| Désactiver les protections Spectre/Meltdown | Récupérerait des performances CPU. | Gain faible dans les jeux et exposition à des failles exploitables. Interdit par FrameCore. | Compromis de sécurité |
| Désactiver Microsoft Defender pendant le jeu | L'antivirus ralentirait les jeux. | L'impact en jeu est négligeable une fois les fichiers analysés. Le Mode Jeu réduit déjà l'activité de fond. Interdit par FrameCore, à tous les niveaux : le niveau Extrême se limite à exclure les dossiers des jeux de l'analyse et à plafonner le processeur des analyses planifiées, la protection en temps réel restant active. | Compromis de sécurité |
| Forcer le mode MSI de la carte graphique | Réduirait la latence. | Les pilotes NVIDIA et AMD récents utilisent déjà les interruptions MSI. Modifier ce paramètre à la main peut empêcher le démarrage de Windows. | Contre-productif |
