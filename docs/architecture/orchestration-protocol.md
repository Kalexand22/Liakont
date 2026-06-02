# Vue d'ensemble du système d'orchestration

_Document de haut niveau. Le protocole exécutable est dans `orchestration/protocol.md`._

## Principe

Le développement de Conformat est piloté par un système d'orchestration multi-agents
repris du projet Stratum :

```
conformat-orchestration/    ← Dépôt d'état central ($ORCH_REPO)
  ├── config.yaml             max_parallel: 3
  ├── state.yaml              Statuts des items (runtime, versionné git)
  ├── events.jsonl            Journal d'audit append-only
  ├── leases/                 slot-N.yaml (verrous d'agents, TTL 30 min)
  └── session-log/            <session>_<item>.md (logs par session)

Conformat/                  ← Dépôt source (ce dépôt) — slot-1
  ├── orchestration/
  │   ├── manifest.yaml       Index du backlog
  │   ├── items/              Détail des items par lot
  │   ├── blueprints/         Graphes de nœuds d'exécution
  │   ├── protocol.md         Protocole pas-à-pas
  │   └── prompt.md           Point d'entrée du mode orchestration
  ├── .claude/
  │   ├── agents/             Subagents (commit, finalize, fix-*)
  │   └── settings.json       ORCH_REPO=C:\Source\conformat-orchestration
  └── src/, tests/, docs/     Le produit

Conformat2/, Conformat3/    ← Clones pour le parallélisme (slots 2 et 3)
```

## Cycle de vie d'un item

```
pending → claimed → in_progress → done
            ↓
          stale → (auto-recovery) → done | stale

gate : pending → gate_pending → (PR humaine + merge) → done
```

## Acteurs

| Acteur | Rôle |
|---|---|
| Orchestrateur (Opus) | Sélectionne l'item, planifie, implémente, pilote les nœuds |
| orch-commit (Haiku) | Compose les messages de commit |
| orch-finalize (Haiku) | Compose les logs de session |
| orch-fix-verify (Sonnet) | Répare les échecs de verify-fast |
| orch-fix-review (Sonnet) | Applique les corrections de review |
| orch-fix-tests (Sonnet) | Répare les tests cassés |
| Humain | Valide les gates, merge les PR, tranche les décisions fiscales |

## Segments et gates

```
socle ──────────────► GATE_SOCLE
                          │
core-foundation ─────► GATE_CORE_FOUNDATION
                          ├──────────────────────┐
pa-client ───────────► GATE_PA_CLIENT            │
                          │                      │
pipeline-hosts ──────► GATE_PIPELINE      adapter-encheresv6 ──► GATE_ADAPTER_ENCHERESV6
                          │                      │
console-admin ───────► GATE_CONSOLE_ADMIN        │
                          │                      │
deployment ──────────► GATE_DEMO_ISATECH ◄───────┘
```

Chaque gate = validation humaine + PR vers main. L'IA ne merge jamais dans main.
