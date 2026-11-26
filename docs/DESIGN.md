# Système de design FrameCore

## Direction artistique

**« Instrument de précision »** : un logiciel qui mesure avant d'affirmer. Fonds graphite profonds, surfaces à filets fins
plutôt qu'à ombres, une seule couleur signature — le **vert « signal » #3BE8A0** — réservée aux actions principales, aux
valeurs positives et à la progression ; un bleu de données (#6EA8FF) pour l'information, ambre et corail pour les
avertissements et les erreurs. Les chiffres sont mis en scène (police d'affichage, chiffres tabulaires, comptage animé),
le texte reste sobre. Pas de verre dépoli, pas de dégradés décoratifs, pas d'illustrations génériques.

## Jetons (`src/FrameCore.UI/Themes`)

| Fichier | Contenu |
|---|---|
| `Colors.Dark.xaml` / `Colors.Light.xaml` | Couleurs sémantiques : `Bg0`, `Bg1`, `Card`, `CardHover`, `Inset`, `Border`, `BorderStrong`, `Text`, `TextMuted`, `TextFaint`, `Accent` (+ `Hover`, `Pressed`, `Soft`, `Text`), `OnAccent`, `Success`, `Warning`, `Danger`, `Info`, `Neutral` (+ variantes `Soft`), `Chart1..4`, `Grid`, `Focus` |
| `Tokens.xaml` | Polices, échelle typographique, espacements (grille de 4 px), rayons, durées d'animation |
| `Icons.xaml` | 68 icônes Lucide converties en géométries WPF (trait 2 px sur grille 24) |
| `Styles.xaml` | Composants : textes, cartes, badges, bannières, boutons, navigation, segments, interrupteurs, cases, champs, listes déroulantes, tableaux, barres de défilement, info-bulles, anneau de focus |
| `Templates.xaml` | Modèles métier : constats, fiches d'optimisation, valeurs actuelle/proposée, lignes de propriétés |

Contrastes mesurés (WCAG) — thème sombre sur carte #14171C : texte principal 15,6:1, secondaire 7,6:1, discret (#7D8492)
4,8:1, vert signal 11,3:1, ambre 10,2:1, corail 6,5:1, texte du bouton principal 12:1. Thème clair sur blanc : texte
principal 18,4:1, secondaire 6,6:1, discret (#666D7A) 5,2:1 (4,8:1 sur le fond de page), vert (#087A55) 5,3:1, texte blanc sur bouton vert 5,3:1, ambre 5,6:1,
rouge 4,5:1. Tous les textes atteignent le niveau AA (4,5:1).

## Typographie

| Rôle | Police | Taille / graisse |
|---|---|---|
| Titres de page, sections, grands chiffres | Space Grotesk | 30 / 24 / 18 px, demi-gras ; chiffres tabulaires |
| Texte, interface | Inter | 13,5 px (corps), 12 px (secondaire), 11 px (légendes, petites capitales) |
| Valeurs techniques (registre, versions, journal) | JetBrains Mono | 11–12 px |

Trois familles seulement, embarquées (SIL OFL 1.1) pour un rendu identique hors ligne ; repli Segoe UI pour les glyphes absents.

## Composants

Boutons (principal, secondaire, discret, danger, petit, icône) avec survol, pression (léger rétrécissement), désactivé
(opacité 42 %) ; cartes (standard, héros, interactive) ; segments pour les choix exclusifs ; interrupteurs ; jauge
annulaire ; **progression par étapes** (un segment par étape réelle, rempli selon la fraction réelle, barre indéterminée
quand la durée est inconnue) ; badges de confiance et de statut ; tableaux de données sobres.

## Mouvement (`Infrastructure/Motion.cs`)

| Animation | Durée | Usage |
|---|---|---|
| Transition de page | 220–280 ms, glissement 14 px + fondu | changement de page |
| Entrée décalée | 260–320 ms, +45 ms par élément | cartes de niveau, tuiles |
| Comptage | 650 ms | FPS, compteurs, indice |
| Barres et jauge | 600–900 ms | barres FPS des jeux, jauge du tableau de bord, progression |
| Survol / pression | 80–180 ms | boutons, navigation |

Aucune animation permanente ; aucune action réelle n'attend une animation ; tout est désactivé quand Windows a les
« Effets d'animation » désactivés (`SPI_GETCLIENTAREAANIMATION`).

## Accessibilité

Anneau de focus visible (2 px) sur tous les contrôles interactifs, navigation au clavier, noms accessibles sur les
boutons-icônes, zones cliquables ≥ 30 px, mise à l'échelle Windows respectée (WPF vectoriel, `PerMonitorV2`), fenêtre
limitée à l'écran disponible, thèmes sombre et clair.

## Outils de conception étudiés

| Outil | Constat dans cette session |
|---|---|
| Penpot (serveur MCP officiel) | non disponible dans l'environnement : aucun serveur Penpot connecté |
| Figma (serveur MCP officiel) | nécessite une authentification non réalisée ; non utilisé |
| Xfield (Orvio) | non vérifiable (produit et licence non accessibles) ; non utilisé |

Le système de design est donc défini directement dans le code (jetons, styles, icônes SVG converties, contrôles dessinés),
ce qui garantit que l'application ne dépend d'aucun service externe. Les captures de chaque écran sont produites
automatiquement par la CI (`FrameCore.exe --ui-tour`) pour vérifier le rendu réel sous Windows.
