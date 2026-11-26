# Rapport de tests — FrameCore 1.0.0

Environnement de développement : conteneur Linux x64 (pas de Windows, pas de GPU, pas d'accès matériel).
.NET SDK 10.0.404, NSIS 3.09. Wine 9.0 est présent mais plante au démarrage (`double free or corruption`) :
**l'interface WPF n'a pas pu être lancée** dans cet environnement.

## 1. Ce qui a été vérifié

| Vérification | Résultat |
|---|---|
| Compilation Debug et Release des 15 projets + tests (`net10.0` / `net10.0-windows`) | ✅ sans erreur |
| Publication self-contained `win-x64` | ✅ `publish/FrameCore/FrameCore.exe` (PE32+ x86-64) |
| Création de l'installateur NSIS (stub natif amd64) | ✅ `artifacts/FrameCore-Setup-1.0.0-x64.exe` |
| Tests unitaires xUnit | ✅ 70 / 70 réussis |
| Contrôle « aucune donnée personnelle dans le paquet » (script de build) | ✅ |

### Détail des tests automatisés

| Classe | Tests | Couverture |
|---|---|---|
| CatalogTests | 11 | Chargement sans erreur, textes FR/EN complets, règles de profil, absence de composant protégé, absence de changement de sécurité interdit, gestionnaires connus, faits utilisés par les conditions, exclusion X3D bi-CCD, cohérence des fichiers de langue et des paramètres de format, rejet d'entrées dangereuses |
| EngineTests | 10 | Application puis restauration exacte (y compris valeurs absentes) ; rollback en cas d'échec au milieu d'un réglage ; journal écrit avant toute modification ; refus d'un réglage incompatible sans rien toucher ; services/tâches/alimentation ; plan Performances ultimes dupliqué puis supprimé ; fusion des réglages DirectX ; vrais réglages du catalogue sur machine simulée ; restauration multiple du plus récent au plus ancien ; séparation expérimental/consentement dans le plan |
| SecurityTests | 4 | Worker : n'accepte que les actions exactes du catalogue ; restrictions des modifications dynamiques ; politique de sécurité bloquant les cibles protégées ; aller-retour du protocole du canal |
| JournalTests | 1 | Le journal survit à un redémarrage et marque les modifications interrompues |
| FrameStatisticsTests | 5 | FPS exacts, 1 % low (moyenne des 1 % les plus lents), 0,1 % low seulement ≥ 10 000 images, percentiles, conversion horodatages → frametimes |
| PresentMonTests | 2 | Colonnes PresentMon 1.x et 2.x, valeurs NA ignorées, colonne manquante signalée |
| ComparisonTests | 2 | Test de Welch, différence dans le bruit non présentée comme un gain |
| BottleneckTests | 3 | Limitation GPU, limitation CPU et VRAM saturée, FPS plafonnés non présentés comme une limite matérielle |
| EdidTests | 2 | Décodage EDID (fabricant, résolution native, plage VRR) |
| MemoryTests | 14 | Décodage des références de kits RAM sans deviner, simple canal et XMP inactif, double canal d'après les emplacements |
| IdentificationTests | 3 | Identification CPU, GPU et carte mère ; support de la version de Windows |
| StartupTests | 3 | Chemin d'exécutable, sémantique StartupApproved, protection des anti-cheats, désactivation sans suppression |
| StressAndTuningTests | 3 | Unité de calcul CPU déterministe ; réglage GPU automatique : recul après le premier échec, plafond, retour à l'origine si la validation finale échoue |
| PlatformParsingTests | 4 | Sortie bcdedit indépendante de la langue, compteurs GPU par adaptateur, codec registre, chaîne de réglages DirectX |

## 2. Ce qui n'a pas pu être testé ici

| Fonction | Raison | Statut |
|---|---|---|
| Lancement de l'interface, navigation, rendu | Pas de Windows ; Wine inutilisable | **Non testé** |
| Détection matérielle réelle (WMI, DXGI, D3DKMT, CPUID…) | Pas de Windows | Non testé (logique d'analyse testée) |
| Application réelle des réglages (registre, services, powercfg, bcdedit) | Pas de Windows ; règle : aucune optimisation sur un PC sans accord | Non testé en réel ; testé sur simulateurs en mémoire |
| Élévation UAC et canal nommé | Pas de Windows | Non testé |
| Overclocking NVIDIA (NVML) / AMD (ADLX) | Pas de GPU | **Non testé — à considérer comme expérimental** |
| Capture ETW / PresentMon | Pas de Windows | Non testé (statistiques testées sur données) |
| Capteurs LibreHardwareMonitor | Pas de matériel | Non testé |
| Installation / désinstallation | Pas de Windows | Non testé (script compilé sans erreur) |

## 3. Recommandations pour la première exécution sur un PC

1. Installer sur un PC de test ou après avoir créé un point de restauration.
2. Lancer un diagnostic et vérifier les valeurs affichées dans **Matériel**.
3. Appliquer le profil le plus léger, vérifier, puis **Tout restaurer** pour valider le cycle complet.
4. Exporter un rapport (Paramètres → Rapport) en cas de problème.
