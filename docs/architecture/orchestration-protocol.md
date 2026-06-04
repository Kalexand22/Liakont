# Vue d'ensemble du système d'orchestration

_Document de haut niveau. Le protocole exécutable est dans `orchestration/protocol.md`._

## Principe

Le développement de Liakont est piloté par un système d'orchestration multi-agents
repris du projet Stratum :

```
liakont-orchestration/    ← Dépôt d'état central ($ORCH_REPO)
  ├── config.yaml             max_parallel: 2
  ├── state.yaml              Statuts des items (runtime, versionné git)
  ├── events.jsonl            Journal d'audit append-only
  ├── leases/                 slot-N.yaml (verrous d'agents, TTL = lease_duration_minutes)
  └── session-log/            <session>_<item>.md (logs par session)

Liakont/                  ← Dépôt source (ce dépôt) — slot-1
  ├── orchestration/
  │   ├── manifest.yaml       Index du backlog
  │   ├── items/              Détail des items par lot
  │   ├── blueprints/         Graphes de nœuds d'exécution
  │   ├── protocol.md         Protocole pas-à-pas
  │   └── prompt.md           Point d'entrée du mode orchestration
  ├── .claude/
  │   ├── agents/             Subagents (commit, finalize, fix-*)
  │   └── settings.json       ORCH_REPO=C:\Source\liakont-orchestration
  └── src/, tests/, docs/     Le produit

Liakont2/                 ← Clone pour le parallélisme (slot 2)
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

## Segments et gates (manifest v6)

Enchaînement des segments (chaque segment se termine par sa gate humaine) :

```
socle ─► core-foundation ─┬─► pa-framework ─┬─► pa-b2brouter ──────────┐
                          │                 ├─► pa-superpdp (non bloquant)
                          │                 └─► pipeline ──────────┐   │
                          │                                        │   │
                          └─► agent ─┬─► adapter-encheresv6 ───────┼───┼──┐
                                     │                             │   │  │
                                     └────────────┬────────────────┘   │  │
                                                  ▼                    │  │
                                             console-web               │  │
                                                  │                    │  │
                                                  ▼                    ▼  ▼
                                          deploiement-toolkit ◄────────┴──┘
                                                  │
                                                  ▼
                                           deploiement-cmp
```

| Segment | Lots | Gate | Débloqué par |
|---|---|---|---|
| socle | SOL | GATE_SOCLE | — |
| core-foundation | PIV, TVA, VAL, TRK, CFG | GATE_CORE_FOUNDATION | GATE_SOCLE |
| pa-framework | PAA | GATE_PA_FRAMEWORK | GATE_CORE_FOUNDATION |
| pa-b2brouter | PAB | GATE_PA_B2BROUTER | GATE_PA_FRAMEWORK |
| pa-superpdp | PAS | GATE_PA_SUPERPDP | GATE_PA_FRAMEWORK |
| pipeline | PIP | GATE_PIPELINE | GATE_PA_FRAMEWORK |
| agent | AGT | GATE_AGENT | GATE_CORE_FOUNDATION |
| adapter-encheresv6 | ADP | GATE_ADAPTER_ENCHERESV6 | GATE_AGENT |
| console-web | API, WEB, SUP | GATE_CONSOLE_WEB | GATE_PIPELINE + GATE_AGENT |
| deploiement-toolkit | OPS, BRD, DOC | GATE_TOOLKIT | GATE_CONSOLE_WEB + GATE_PA_B2BROUTER + GATE_ADAPTER_ENCHERESV6 |
| deploiement-cmp | CMP | GATE_PROD_CMP | GATE_TOOLKIT + GATE_ADAPTER_ENCHERESV6 + GATE_PA_B2BROUTER |

Notes :
- **GATE_PA_SUPERPDP n'est PAS bloquante** pour GATE_TOOLKIT (décision 2026-06-02) : Super PDP
  rejoint le produit dès que sa gate passe, sans bloquer la release.
- **GATE_DEMO_ISATECH** est une gate intra-segment du segment deploiement-cmp (checkpoint humain
  « démo prête », sans PR — voir MANIFEST-CONVENTIONS.md règle 9) ; la gate de segment est
  GATE_PROD_CMP.

**Fin du PRODUIT = GATE_TOOLKIT.** Le segment deploiement-cmp ne produit aucun code :
c'est la preuve que le produit se déploie par pur paramétrage.

Chaque gate de segment = validation humaine + PR vers main. L'IA ne merge jamais dans main.
