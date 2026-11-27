# Guide de restauration

FrameCore offre trois niveaux de retour arrière, du plus précis au plus global.

## 1. Le journal de FrameCore (recommandé)

Avant chaque modification, FrameCore **lit et enregistre la valeur exacte d'origine** (y compris « valeur absente »)
dans `%LOCALAPPDATA%\FrameCore\Backups`. La restauration réécrit cette valeur puis **relit** le système pour vérifier.

Dans la page **Restauration** :

| Action | Effet |
|---|---|
| **Restaurer** sur une ligne de l'historique | Annule une seule modification |
| **Restaurer une catégorie** | Annule toutes les modifications actives d'une catégorie (ex. Services). Seules les catégories qui ont encore des modifications actives sont proposées, avec leur nombre |
| **Profils appliqués → Restaurer** | Annule tout un profil appliqué en une fois, du plus récent au plus ancien. Chaque profil indique ce qui est encore actif ; un profil déjà annulé affiche « entièrement restauré » |
| **Tout restaurer** | Annule toutes les modifications actives faites par FrameCore |
| **Détails** | Affiche chaque action et la valeur d'origine enregistrée |

Les modifications sont restaurées dans l'ordre inverse de leur application. Une restauration partielle (une action
impossible à rétablir) est signalée avec la cause ; les autres actions sont tout de même restaurées.

Un réglage peut aussi être restauré depuis sa fiche (Mode Pro ou Optimisation) : bouton **Restaurer**.

### Opérations non réversibles

Certaines opérations de maintenance ne peuvent pas être « annulées » par nature et sont signalées comme telles
avant exécution : suppression des fichiers temporaires, optimisation TRIM (retrim). Elles ne modifient aucun réglage.

### Application interrompue

Si le PC s'éteint pendant une application, la modification en cours reste restaurable (état **Échec**) et le lot est
signalé sur le tableau de bord et dans **Restauration → Optimisation interrompue** : **Reprendre** applique uniquement
les réglages restants (jamais deux fois le même), **Ignorer** clôt le lot. Rien n'est rejoué automatiquement.

### Vérification et réparation

Chaque modification est relue après application. **Restauration → Vérifier et réparer maintenant** relit toutes les
modifications actives (utile après une mise à jour de Windows) : une valeur que Windows n'a pas conservée est réécrite au
plus deux fois, puis la valeur d'origine est restaurée et la raison est notée dans l'historique. Après un redémarrage,
les modifications qui en avaient besoin sont relues automatiquement.

## 2. Exporter / importer les sauvegardes

**page Restauration → Exporter les sauvegardes** crée une archive `.zip` du journal. Utile avant une
réinstallation de FrameCore ou pour conserver un historique. **Importer** recharge ces entrées (sur le même PC).

> La désinstallation de FrameCore conserve vos données par défaut. Si vous cochez « Supprimer aussi mes données »,
> restaurez d'abord vos optimisations : sans journal, FrameCore ne peut plus revenir automatiquement en arrière.

## 3. Point de restauration Windows

Par défaut, FrameCore crée un point de restauration système avant chaque profil (si la Protection du système est
activée sur le disque C:). En dernier recours :

1. Menu Démarrer → tapez **Créer un point de restauration** → bouton **Restauration du système** ;
2. choisissez le point « FrameCore: <nom du profil> » ;
3. si Windows ne démarre plus : maintenez **Maj** en cliquant sur *Redémarrer* → *Dépannage* → *Options avancées*
   → *Restauration du système*.

Windows limite la création à un point toutes les 24 h par défaut ; FrameCore le signale si la création est refusée.

## 4. Cas particuliers

| Situation | Que faire |
|---|---|
| Overclocking GPU instable | Page Overclocking → **Réglages d'usine**. NVIDIA : les offsets appliqués via NVML ne survivent pas à un redémarrage (sauf si un autre logiciel, ex. MSI Afterburner, les réapplique). AMD : les réglages ADLX sont conservés par le pilote ; après un plantage, le pilote AMD rétablit en général lui-même les valeurs par défaut — sinon, Logiciel AMD Adrenalin → Performances → Réglage → *Réinitialiser*. |
| Programme désactivé au démarrage qui manque | Mode Pro → Démarrage → réactiver l'entrée (ou Paramètres Windows → Applications → Démarrage). |
| Service désactivé nécessaire à une application | page Restauration → restaurer la ligne du service (le type de démarrage d'origine est rétabli). |
| Plan d'alimentation | Restaurer la modification : l'ancien plan actif est réactivé. Ou `powercfg /restoredefaultschemes` en invite administrateur. |
| Réglage BCD supprimé (ex. `useplatformclock`) | La restauration réécrit la valeur d'origine ; un redémarrage est nécessaire. |
