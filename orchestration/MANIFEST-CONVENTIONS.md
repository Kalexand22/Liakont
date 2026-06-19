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

## Champ `repo:` au niveau segment (réservé — ADR-0005)

Un segment peut cibler un **dépôt Git autre** que le dépôt plateforme courant. Le champ `repo:`,
au niveau d'un `segments.<nom>`, déclare ce dépôt cible.

```yaml
segments:
  agent:
    branch: feat/agent
    base: main
    repo: liakont-agent      # ← dépôt cible (défaut implicite : dépôt plateforme courant)
    lots: [AGT]
    gate: GATE_AGENT
```

- **Défaut** : champ **omis** ⇒ le segment vit dans le dépôt plateforme courant (`Liakont`). C'est
  le cas de TOUS les segments aujourd'hui — l'agent n'a PAS encore basculé dans son dépôt séparé.
- **Réservé, pas encore actif** : la bascule de l'agent vers `liakont-agent` est **différée**
  (ADR-0005). Tant qu'elle n'est pas faite, **ne renseigner `repo:` sur aucun segment**. Le champ est
  spécifié ici pour qu'au démarrage du chantier de migration on dispose d'une convention stable
  (segments `agent`, `adapter-encheresv6`, part agent de `deploiement-toolkit` → `repo: liakont-agent`).
- **Prérequis d'activation** : l'orchestration multi-repo (`protocol.md` : quel clone pour quel
  `repo:`, `build-agent-context`, merge-back, partage `$ORCH_REPO`) doit être étendue AVANT de
  renseigner le champ — sinon le runner, qui suppose un dépôt unique, route vers le mauvais clone.
  Voir ADR-0005 §Conséquences (réévaluation 2026-06-20).

## Règles

1. **Pas de titres dans le manifest** — l'agent lit `items/<lot>.yaml` pour le titre + la description.
   Le manifest est un index de routage et de résolution de dépendances, rien d'autre.
2. **Pas de commentaires décoratifs** — pas d'en-têtes `# ════`, pas de références d'ADR, pas de
   notes de conception. Un seul séparateur `# ── LOT ──` par groupe de lot.
3. **Pas de segments archivés** — supprimer de `segments:` quand archivé. Les références d'archive
   restent uniquement dans `orchestration/archive/` (dépôt source — jamais dans `$ORCH_REPO`).
4. **Pas de `executor: claude`** — c'est le défaut. Depuis manifest v11, **toute gate de SEGMENT
   ENCORE ACTIVE est HUMAINE** (`executor: human`) : le runner fait les checks + ouvre la PR, la CI
   rejoue verify+tests sur la PR, l'humain merge, et `tools/orch-reconcile-gates.ps1` bascule la
   gate à `done` sur PR mergée (protocol.md Step 1.4). Le blueprint `auto-gate-item` (la gate qui
   mergeait elle-même dans main) est **déprécié** — il dégradait systématiquement en `blocked`,
   merger dans main étant une action humaine (barrière Claude Code + règle CLAUDE.md « human merge
   mandatory »). Seules GATE_CORE_FOUNDATION et GATE_PA_FRAMEWORK le portent encore (déjà `done`,
   terminales) ; ne plus le référencer sur une nouvelle gate.
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
10. **Champ `repo:` au niveau segment** — réservé pour la bascule de l'agent vers un dépôt séparé
    (ADR-0005). Omis par défaut (= dépôt plateforme courant). **Ne pas le renseigner** tant que
    l'orchestration multi-repo n'est pas livrée. Voir la section dédiée ci-dessus.
