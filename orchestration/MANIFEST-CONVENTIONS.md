# Conventions du Manifest

Règles d'édition de `manifest.yaml`. Objectif : garder le fichier sous 200 lignes / 5000 tokens
pour que chaque agent d'orchestration puisse le lire en une seule passe.

## Defaults (omettre quand la valeur correspond)

| Champ      | Valeur par défaut   | Spécifier seulement quand...        |
|------------|--------------------|--------------------------------------|
| `type`     | `work`             | l'item est un `gate`                 |
| `executor` | `claude`           | l'item est `human` (gates)           |
| `blueprint`| `module-work-item` | l'item utilise un autre blueprint    |
| `title`    | *(omis)*           | **jamais dans le manifest** — vit dans `items/<lot>.yaml` |

## Format d'item

YAML inline minimal. Un item par ligne :

```yaml
- { id: PIV01, lot: PIV, priority: 2000, depends_on: [] }
- { id: SOL03, lot: SOL, blueprint: docs-spec-item, priority: 1020, depends_on: [SOL01] }
- { id: GATE_CORE_FOUNDATION, type: gate, executor: human, priority: 2900, depends_on: [TRK05] }
```

## Qui va OÙ

| Information                                    | Emplacement                       |
|------------------------------------------------|-----------------------------------|
| Item id, lot, priority, depends_on, blueprint  | `manifest.yaml` (index)           |
| Titre, description, critères d'acceptation     | `items/<lot>.yaml` (détail)       |
| Statut courant, compteur de retry              | `$ORCH_REPO/state.yaml` (runtime) |
| Segments archivés / terminés                   | répertoire `archive/`             |

## Règles

1. **Pas de titres dans le manifest** — l'agent lit `items/<lot>.yaml` pour le titre + la description.
   Le manifest est un index de routage et de résolution de dépendances, rien d'autre.
2. **Pas de commentaires décoratifs** — pas d'en-têtes `# ════`, pas de références d'ADR, pas de
   notes de conception. Un seul séparateur `# ── LOT ──` par groupe de lot.
3. **Pas de segments archivés** — supprimer de `segments:` quand archivé. Les références d'archive
   restent uniquement dans le répertoire `archive/`.
4. **Pas de `executor: claude`** — c'est le défaut. Spécifier uniquement `executor: human` sur les gates.
5. **Pas de `type: work`** — c'est le défaut. Spécifier uniquement `type: gate` sur les gates.
6. **Pas de `blueprint: module-work-item`** — c'est le défaut. Spécifier uniquement les blueprints
   non-défaut (`docs-spec-item`, `tooling-item`, `wpf-screen-item`).
7. **Purger les items terminés** — quand tous les items d'un lot sont done et que la gate est validée,
   déplacer toute la section du lot vers `archive/` et la supprimer du manifest.
8. **Bump de version** — incrémenter `meta.version` à chaque changement structurel du manifest.
