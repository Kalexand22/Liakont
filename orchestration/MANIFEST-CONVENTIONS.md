# Conventions du Manifest

Règles d'édition de `manifest.yaml`. Objectif : garder le fichier sous 200 lignes / 5000 tokens
pour que chaque agent d'orchestration puisse le lire en une seule passe.

## Defaults (omettre quand la valeur correspond)

| Champ      | Valeur par défaut   | Spécifier seulement quand...        |
|------------|--------------------|--------------------------------------|
| `type`     | `work`             | l'item est un `gate`                 |
| `executor` | `claude`           | l'item est `human` (gates HUMAINES ; les gates AUTO gardent `claude`) |
| `blueprint`| `module-work-item` | l'item utilise un autre blueprint (gate AUTO ⇒ `auto-gate-item`) |
| `title`    | *(omis)*           | **jamais dans le manifest** — vit dans `items/<lot>.yaml` |

## Format d'item

YAML inline minimal. Un item par ligne :

```yaml
- { id: PIV01, lot: PIV, priority: 2000, depends_on: [] }
- { id: SOL03, lot: SOL, blueprint: docs-spec-item, priority: 1020, depends_on: [SOL01] }
- { id: GATE_CORE_FOUNDATION, type: gate, blueprint: auto-gate-item, priority: 2900, depends_on: [TRK05] }  # gate AUTO : merge auto vers main sur vert
- { id: GATE_AGENT, type: gate, executor: human, priority: 5900, depends_on: [AGT05] }  # gate HUMAINE : PR pour merge humain
```

## Qui va OÙ

| Information                                    | Emplacement                       |
|------------------------------------------------|-----------------------------------|
| Item id, lot, priority, depends_on, blueprint  | `manifest.yaml` (index)           |
| Titre, description, critères d'acceptation     | `items/<lot>.yaml` (détail)       |
| Statut courant, compteur de retry              | `$ORCH_REPO/state.yaml` (runtime) |
| Définitions de lots/segments archivés (terminés) | `orchestration/archive/` (CE dépôt source) |
| Données runtime archivées (sessions, états purgés) | `$ORCH_REPO/archive/` (dépôt d'état)  |

## Règles

1. **Pas de titres dans le manifest** — l'agent lit `items/<lot>.yaml` pour le titre + la description.
   Le manifest est un index de routage et de résolution de dépendances, rien d'autre.
2. **Pas de commentaires décoratifs** — pas d'en-têtes `# ════`, pas de références d'ADR, pas de
   notes de conception. Un seul séparateur `# ── LOT ──` par groupe de lot.
3. **Pas de segments archivés** — supprimer de `segments:` quand archivé. Les références d'archive
   restent uniquement dans `orchestration/archive/` (dépôt source — jamais dans `$ORCH_REPO`).
4. **Pas de `executor: claude`** — c'est le défaut. Spécifier `executor: human` sur les gates HUMAINES.
   Une gate AUTOMATIQUE (intégration auto vers main, voir protocol.md Step 5c) garde le défaut
   `claude` et porte `blueprint: auto-gate-item` (ex. GATE_CORE_FOUNDATION, GATE_PA_FRAMEWORK,
   GATE_PIPELINE, GATE_ADAPTER_ENCHERESV6). GATE_SOCLE est HUMAINE par exception (manifest v10) :
   son diff vs main contient tout le socle Stratum vendored → review d'intégration > 1M tokens.
5. **Pas de `type: work`** — c'est le défaut. Spécifier uniquement `type: gate` sur les gates.
6. **Pas de `blueprint: module-work-item`** — c'est le défaut. Spécifier uniquement les blueprints
   non-défaut (`docs-spec-item`, `tooling-item`, `blazor-page-item`).
7. **Purger les items terminés** — quand tous les items d'un lot sont done et que la gate est validée,
   déplacer toute la section du lot vers `orchestration/archive/` et la supprimer du manifest.
8. **Bump de version** — incrémenter `meta.version` à chaque changement structurel du manifest.
9. **Gates intra-segment** — une gate qui n'est la `gate:` d'aucun segment (ex. `GATE_DEMO_ISATECH`)
   est un checkpoint humain SANS PR : le protocole ne crée des PR que pour les gates de segment.
   L'opérateur passe une gate intra-segment à `done` via `orch-state.ps1` quand la condition
   humaine est remplie (ex. démo déroulée). Les items en aval restent bloqués d'ici là — c'est voulu.
