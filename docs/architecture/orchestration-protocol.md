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
  ├── leases/                 slot-N.yaml (verrous d'agents, TTL = lease_duration_minutes)
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

## Segments et gates (manifest v2)

```
socle ───────────────────► GATE_SOCLE
                               │
core-foundation ──────────► GATE_CORE_FOUNDATION
   (pivot, TVA, validation,    ├────────────────────────────────────┐
    tracking, archivage)       │                                    │
                               │                          adapter-encheresv6
pa-framework ─────────────► GATE_PA_FRAMEWORK                       │
   (IPaClient + capacités      ├──────────────┬─────────────┐       │
    + tests de contrat)        │              │             │       ▼
                               │              │             │  GATE_ADAPTER_ENCHERESV6
pipeline ─────────────────► GATE_PIPELINE     │             │       │
                               │         pa-b2brouter   pa-superpdp │
service-api ──────────────► GATE_SERVICE_API  │             │       │
   (Service + API HTTP + CLI)  │              ▼             ▼       │
                               │     GATE_PA_B2BROUTER  GATE_PA_SUPERPDP
console-admin ────────────► GATE_CONSOLE_ADMIN│                     │
   (WPF cliente de l'API)      │              │                     │
                               │              │                     │
deploiement-toolkit ──────► GATE_TOOLKIT      │                     │
   (packaging + docs)          │              │                     │
                               ▼              ▼                     ▼
deploiement-cmp ──────────► GATE_DEMO_ISATECH ──► GATE_PROD_CMP
   (PARAMÉTRAGE pur : table TVA CMP, config, démo, mise en prod)
```

**Fin du PRODUIT = GATE_TOOLKIT.** Le segment deploiement-cmp ne produit aucun code :
c'est la preuve que le produit se déploie par pur paramétrage.

Chaque gate = validation humaine + PR vers main. L'IA ne merge jamais dans main.
