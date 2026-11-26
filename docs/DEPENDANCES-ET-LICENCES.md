# Dépendances et licences

Inventaire établi à partir du fichier `FrameCore.deps.json` produit par la publication et des métadonnées
(`.nuspec`) des paquets NuGet restaurés. Date de relevé : 2026-11-26.

FrameCore lui-même est distribué sous licence **MIT** (fichier `LICENSE`).

## 1. Composants redistribués dans l'installateur

| Composant | Version | Auteur / projet | Licence | Rôle dans FrameCore |
|---|---|---|---|---|
| .NET Runtime + Windows Desktop Runtime (WPF) | 10.0.x | Microsoft / .NET Foundation | MIT | Runtime embarqué (*self-contained*) |
| CommunityToolkit.Mvvm | 8.4.0 | Microsoft / .NET Foundation | MIT | MVVM (générateurs de code) |
| Microsoft.Extensions.DependencyInjection (+ Abstractions) | 10.0.15 | Microsoft | MIT | Injection de dépendances |
| Microsoft.Extensions.Logging / Options / Primitives | 6.0.0 | Microsoft | MIT | Dépendances transitives de LibreHardwareMonitorLib |
| System.Management | 10.0.15 | Microsoft | MIT | Requêtes WMI |
| System.ServiceProcess.ServiceController | 10.0.15 | Microsoft | MIT | Lecture des services |
| System.Diagnostics.EventLog | 10.0.15 | Microsoft | MIT | Lecture des erreurs WHEA |
| System.IO.Ports | 10.0.3 | Microsoft | MIT | Dépendance transitive de LibreHardwareMonitorLib |
| Microsoft.Diagnostics.Tracing.TraceEvent | 3.2.8 | Microsoft (PerfView) | MIT | Session ETW de capture des images |
| Microsoft.Diagnostics.NETCore.Client | 0.2.510501 | Microsoft | MIT | Dépendance transitive de TraceEvent |
| KernelTraceControl.dll, msdia140.dll (dossier `amd64`) | — | Microsoft | Fichiers redistribués par le paquet TraceEvent (MIT) | Assistants natifs de TraceEvent |
| LibreHardwareMonitorLib | 0.9.6 | LibreHardwareMonitor | **MPL-2.0** | Capteurs avancés (températures, puissance) |
| BlackSharp.Core | 1.0.7 | Blacktempel | **MPL-2.0** | Dépendance de LibreHardwareMonitorLib |
| DiskInfoToolkit | 1.1.2 | Blacktempel | **MPL-2.0** | Dépendance de LibreHardwareMonitorLib |
| RAMSPDToolkit-NDD | 1.4.2 | Blacktempel | **MPL-2.0** | Lecture SPD (via LibreHardwareMonitorLib) |
| HidSharp | 2.6.4 | James F. Bellinger | Apache-2.0 | Dépendance de LibreHardwareMonitorLib |
| Mono.Posix.NETStandard | 1.0.0 | Mono Project / Microsoft | MIT | Dépendance transitive de LibreHardwareMonitorLib (non utilisée sous Windows) |
| Vortice.Direct3D11 / DXGI / DirectX / D3DCompiler | 3.8.3 | Amer Koleci | MIT | Test de stabilité GPU (calcul D3D11) |
| Vortice.Mathematics | 2.1.0 | Amer Koleci | MIT | Dépendance de Vortice |
| SharpGen.Runtime / SharpGen.Runtime.COM | 2.4.2-beta | SharpGenTools | MIT | Dépendance de Vortice |

### Obligations

- **MIT / Apache-2.0** : conserver les mentions de copyright et le texte de licence → `THIRD_PARTY_NOTICES.md`.
- **MPL-2.0** (licence copyleft *au niveau fichier*) : les bibliothèques sont utilisées **sans modification**, sous forme
  binaire. FrameCore indique où obtenir leur code source (dépôts GitHub cités dans `THIRD_PARTY_NOTICES.md`). Toute
  modification de ces fichiers devrait être publiée sous MPL-2.0. L'usage de bibliothèques MPL depuis un programme MIT
  est compatible.

## 2. Composants NON redistribués (installés par l'utilisateur s'il le souhaite)

| Outil | Licence | Raison de la non-inclusion | Comment FrameCore l'utilise |
|---|---|---|---|
| Intel PresentMon | MIT (code), binaire signé Intel | Non téléchargé automatiquement ; l'utilisateur choisit la version officielle | Chemin configuré par l'utilisateur ; **signature Authenticode Intel vérifiée** avant chaque exécution |
| PawnIO (pilote signé, projet namazso/PawnIO) | Licence open source du projet (voir pawnio.eu) | Pilote noyau : installation volontaire uniquement | Détecté ; active les capteurs avancés de LibreHardwareMonitor |
| NVIDIA NVML (`nvml.dll`) | Propriétaire NVIDIA, fourni avec le pilote | Fait partie du pilote NVIDIA | Chargé depuis `System32` s'il existe |
| AMD ADLX (`amdadlx64.dll`) | Propriétaire AMD, fourni avec le pilote | Fait partie du pilote AMD | Chargé depuis `System32` s'il existe |

## 3. Outils de développement (non distribués)

| Outil | Licence | Usage |
|---|---|---|
| .NET SDK 10 | MIT | Compilation |
| NSIS 3.09 | zlib/libpng (+ bzip2, CPL pour certains plugins) | Création de l'installateur. Le stub NSIS inclus dans l'installateur est sous licence zlib : aucune obligation pour l'installateur produit. |
| xUnit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.0.1 | Apache-2.0 / MIT | Tests unitaires |
| pandoc (facultatif) | GPL-2.0+ | Conversion de la documentation en HTML au moment du build (le résultat n'est pas soumis à la GPL) |

## 4. Ressources graphiques (relevé du 2026-11-26)

| Ressource | Version | Auteur / projet | Source | Licence | Attribution |
|---|---|---|---|---|---|
| Police Inter (400–700) | via @fontsource/inter 5.3.0 | The Inter Project Authors | github.com/rsms/inter | SIL OFL 1.1 | texte de licence fourni (`licenses/OFL-Inter.txt`) |
| Police Space Grotesk (500–700) | via @fontsource/space-grotesk 5.3.0 | The Space Grotesk Project Authors | github.com/floriankarsten/space-grotesk | SIL OFL 1.1 | `licenses/OFL-SpaceGrotesk.txt` |
| Police JetBrains Mono (400–600) | via @fontsource/jetbrains-mono 5.3.0 | The JetBrains Mono Project Authors | github.com/JetBrains/JetBrainsMono | SIL OFL 1.1 | `licenses/OFL-JetBrainsMono.txt` |
| Icônes Lucide (68) | lucide-static 1.81.0 | Lucide Contributors (une partie issue de Feather, Cole Bemis) | lucide.dev | ISC (+ MIT pour les icônes Feather) | `licenses/LICENSE-Lucide.txt` |
| Icône et logo FrameCore | — | création originale | dépôt | MIT | — |

Les polices sont des sous-ensembles « latin » (français et anglais) convertis de WOFF2 en TTF ; seules les métadonnées
de nom ont été harmonisées (aucun « Reserved Font Name » n'est déclaré par ces polices). Aucune illustration tierce n'est
utilisée : les visuels (jauge, barres, progression) sont dessinés par le code. Aucun outil de design externe (Penpot,
Figma) n'était connecté à cette session ; le système de design est entièrement défini dans le projet
(`src/FrameCore.UI/Themes/Tokens.xaml`, `Colors.*.xaml`, `Styles.xaml`, `Icons.xaml`).
