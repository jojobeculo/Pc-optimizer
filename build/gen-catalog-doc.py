#!/usr/bin/env python3
"""Generates docs/CATALOGUE.md from catalog/tweaks/*.json and catalog/knowledge/rejected-tweaks.json.
Usage: python3 build/gen-catalog-doc.py"""
import json, glob, os, collections
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
fr = json.load(open(os.path.join(ROOT, "lang", "fr.json"), encoding="utf-8"))
T = lambda k, d=None: fr.get(k, d if d is not None else k)
CONF = {"Confirmed": "Confirmé", "Probable": "Probable", "Experimental": "Expérimental", "NotRecommended": "Non recommandé"}
RISK = {"None": "Nul", "Low": "Faible", "Medium": "Moyen", "High": "Élevé"}
PROF = {"Balanced": "Équilibré", "Performance": "Performances", "Maximum": "Performance maximale"}
tweaks = []
for f in sorted(glob.glob(os.path.join(ROOT, "catalog", "tweaks", "*.json"))):
    tweaks += json.load(open(f, encoding="utf-8"))["tweaks"]
def action_text(a):
    t = a["type"]
    if t == "registry": return f"Registre `{a['path']}\\{a['name']}` = `{a.get('value', '(suppression)')}` ({a.get('valueKind', '')})"
    if t == "service": return f"Service `{a['name']}` → démarrage `{a.get('startType')}`"
    if t == "task": return f"Tâche planifiée `{a.get('path', '')}{a.get('name', '')}` → {'activée' if a.get('enabled') else 'désactivée'}"
    if t == "powerSetting": return f"Plan d'alimentation : paramètre `{a['setting']}` (sous-groupe `{a['subgroup']}`) secteur={a.get('ac')} batterie={a.get('dc')}"
    if t == "powerScheme": return f"Activer le plan `{a['scheme']}`" + (f" (créé par duplication de `{a['duplicateFrom']}` si absent)" if a.get("duplicateFrom") else "")
    if t == "powerOverlay": return f"Mode d'alimentation `{a.get('overlay')}`"
    if t == "bcdRemove": return f"Supprimer l'option BCD forcée `{a['name']}`"
    if t == "spi": return f"SystemParametersInfo `{a['name']}` = `{a.get('value')}`"
    if t == "dxSetting": return f"Réglage DirectX `{a['name']}` = `{a.get('value')}`"
    if t == "optionalFeature": return f"Fonctionnalité Windows `{a['name']}` → {'activée' if a.get('enabled') else 'désactivée'}"
    if t == "netAdapterProperty": return f"Carte réseau physique : propriété `{a['name']}` = `{a.get('value')}`"
    if t == "handler": return f"Opération `{a['name']}`"
    return t
def cond_text(c):
    if "any" in c and c["any"]: return "au moins une de : " + " ; ".join(cond_text(x) for x in c["any"])
    v = c.get("value")
    return f"`{c['fact']} {c['op']}`" if v is None else f"`{c['fact']} {c['op']} {v}`"
out = ["# Catalogue des optimisations", "",
       "Généré automatiquement depuis `catalog/tweaks/*.json` (`python3 build/gen-catalog-doc.py`).",
       f"**{len(tweaks)} optimisations.** La valeur initiale est lue sur chaque machine au moment du diagnostic et enregistrée avant toute modification ; "
       "elle apparaît dans FrameCore (Mode Pro) et dans le journal de restauration.", "",
       "Méthode commune à toutes les fiches : (1) lecture de la valeur actuelle, (2) vérification de la compatibilité, (3) enregistrement de l'état initial, "
       "(4) application, (5) relecture, (6) vérification de la prise en compte, (7) journalisation. En cas d'échec, les actions déjà faites sont annulées.", "",
       "## Synthèse", "", "| Identifiant | Nom | Catégorie | Confiance | Risque | Profils | Peut influencer |", "|---|---|---|---|---|---|---|"]
for t in tweaks:
    out.append(f"| `{t['id']}` | {t['name']['fr']} | {T('pro.cat.' + t['category'], t['category'])} | {CONF[t['confidence']]} | {RISK.get(t.get('risk', 'None'))} | "
               f"{', '.join(PROF[p] for p in t.get('profiles', [])) or 'Mode Pro uniquement'} | {', '.join(T('impact.' + a) for a in t.get('impactAreas', []))} |")
out += ["", "## Fiches détaillées", ""]
for t in tweaks:
    out += [f"### {t['name']['fr']} (`{t['id']}`)", "",
            f"- **Catégorie** : {T('pro.cat.' + t['category'], t['category'])} — **Objectif** : {t.get('objective', {}).get('fr', '—')}",
            f"- **Description** : {t['description']['fr']}",
            f"- **Impact attendu** : {t['impact']['fr']}",
            f"- **Peut influencer** : {', '.join(T('impact.' + a) for a in t.get('impactAreas', []))}"
            + ("" if any(a in ('Fps', 'FrameTimes') for a in t.get('impactAreas', [])) else " — *aucune raison technique de modifier les FPS*"),
            f"- **Régressions possibles** : {t['regressions']['fr']}",
            f"- **Confiance** : {CONF[t['confidence']]} — **Risque** : {RISK.get(t.get('risk', 'None'))} — **Redémarrage** : {T('restart.' + t.get('restart', 'None'), t.get('restart', 'None'))}",
            f"- **Persistance** : {T('persistence.' + t.get('persistence', 'Persistent'))}",
            f"- **Profils** : {', '.join(PROF[p] for p in t.get('profiles', [])) or 'Mode Pro uniquement'}" + (" — validation spécifique requise" if t.get('requiresConsent') else ""),
            f"- **Compatibilité** : " + (" ET ".join(cond_text(c) for c in t.get('conditions', [])) or "toutes les machines Windows 10/11 x64"),
            f"- **Conflits** : {', '.join('`' + c + '`' for c in t.get('conflicts', [])) or 'aucun'}",
            "- **Application** :"]
    out += [f"  - {action_text(a)}" for a in t["actions"]]
    types = sorted({a["type"] for a in t["actions"]})
    out.append("- **Vérification** : " + " ; ".join(T("method.verify." + x) for x in types))
    out.append("- **Restauration** : " + (" ; ".join(T("method.restore." + x) for x in types) if t.get("reversible", True) else T("method.restore.none")))
    if t.get("sources"): out.append("- **Sources** : " + " · ".join(t["sources"]))
    out.append("")
rej = json.load(open(os.path.join(ROOT, "catalog", "knowledge", "rejected-tweaks.json"), encoding="utf-8"))["entries"]
out += ["## Réglages écartés", "", "Astuces répandues que FrameCore n'applique pas.", "", "| Réglage | Ce qu'on en dit | Ce qu'il en est | Verdict |", "|---|---|---|---|"]
for r in rej:
    out.append(f"| {r['name']['fr']} | {r['claim']['fr']} | {r['verdict']['fr']} | {T('rejected.kind.' + r['kind'])} |")
open(os.path.join(ROOT, "docs", "CATALOGUE.md"), "w", encoding="utf-8").write("\n".join(out) + "\n")
print(f"{len(tweaks)} tweaks, {len(rej)} rejected")
