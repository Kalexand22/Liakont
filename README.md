# Liakont

**Passerelle de conformité facturation électronique pour logiciels métier legacy.**

Liakont est une Solution Compatible (SC) qui permet à un logiciel métier legacy
(Magic XPA/Pervasive, AS400, vieux SQL Server, Access...) de répondre aux obligations
de la réforme française de facturation électronique (e-invoicing / e-reporting,
échéances septembre 2026 et 2027) **sans modifier le logiciel source** :
extraction en lecture seule de la base, normalisation EN 16931, mapping TVA validé,
contrôles qualité, envoi vers une Plateforme Agréée (B2Brouter), piste d'audit 10 ans.

## État du projet

🚧 **Phase 1 — conception terminée, développement à lancer.**
Le backlog complet est dans `orchestration/manifest.yaml` (52 items répartis en 7 segments).

## Structure du dépôt

```
blueprint.md            — Doctrine d'architecture du produit (à lire en premier)
CLAUDE.md / AGENTS.md   — Instructions de travail pour les agents IA
orchestration/          — Système d'orchestration multi-agents
  manifest.yaml           Backlog (index : items, segments, gates, dépendances)
  protocol.md             Protocole d'exécution autonome
  blueprints/             Graphes de nœuds par type d'item
  items/                  Détail des items par lot (description + critères d'acceptation)
.claude/                — Configuration Claude Code + subagents d'orchestration
docs/
  conception/           — Specs fonctionnelles F01-F12 (source de vérité produit)
  market/               — Analyse d'opportunité, marché, offre commerciale (DR7-DR17)
  architecture/         — Conventions du repo (à produire : SOL04)
  adr/                  — Architecture Decision Records
tools/                  — Scripts de vérification et d'orchestration
tasks/                  — Plans de travail, leçons apprises
src/                    — Code source (à produire — voir blueprint.md pour la structure cible)
tests/                  — Tests (à produire)
```

## Architecture cible (résumé)

Liakont est un **produit générique** avec deux axes de plug-ins symétriques :
les **sources** (IExtractor) et les **Plateformes Agréées** (IPaClient + capacités).
Toute donnée client est du **paramétrage** (`deployments/<client>/`), jamais du code.

```
Gateway.sln
├─ Gateway.Core/                       ★ LE PRODUIT (générique)
│  ├─ Pivot/                             Modèle pivot EN 16931 + contrat IExtractor
│  ├─ TvaMapping/                        Moteur de mapping TVA (tables = paramétrage)
│  ├─ Validation/                        Contrôles qualité pré-envoi (~20 règles)
│  ├─ Tracking/                          Multi-provider (SQLite/SQL Server) : états, piste
│  │                                     d'audit + coffre d'archivage fiscal 10 ans (WORM)
│  ├─ PaClient/                          Abstraction IPaClient + PaCapabilities
│  ├─ Pipeline/                          extract → check → send → sync
│  └─ Configuration/                     Config JSON + secrets DPAPI
├─ Gateway.PaClients.B2Brouter/        ★ Plug-in PA #1 (staging validé)
├─ Gateway.PaClients.SuperPdp/         ★ Plug-in PA #2 (Offre Éco marque grise)
├─ Gateway.Adapters.EncheresV6/        ★ Plug-in source #1 (Magic XPA / Pervasive)
├─ Gateway.Api/ + Gateway.ApiClient/     Contrats + client de l'API HTTP
├─ Gateway.Service/                      L'HÔTE : service Windows = ordonnanceur + pipeline
│                                        + API HTTP + seul accès à la base (mono-écrivain)
├─ Gateway.App/                          Console WPF multi-postes (cliente de l'API)
└─ Gateway.Cli/                          Utilitaire de mise en service et de secours

deployments/cmp/                       ★ Paramétrage du 1er déploiement (CMP) — pas du code
```

Stack : **.NET Framework 4.8** (compatibilité Windows legacy), WPF, SQLite/SQL Server Express,
Newtonsoft.Json, Dapper. Déploiement : **on-premise uniquement**.
Voir `blueprint.md` pour la doctrine complète.

## Orchestration multi-agents

Le développement est piloté par un système d'orchestration multi-agents (principe Stratum) :

- **Ce dépôt** contient le backlog, les blueprints d'exécution et le code.
- **`C:\Source\liakont-orchestration`** (dépôt séparé, `$ORCH_REPO`) contient l'état
  runtime : statuts des items, leases de slots, journal d'événements, logs de sessions.
- Chaque agent Claude Code tourne dans son propre clone (`Liakont`, `Liakont2`, ...),
  réclame un slot, prend un item éligible, l'implémente, le vérifie, le fait reviewer,
  le merge et libère son slot.

### Lancer une session d'orchestration

Dans un clone du dépôt, démarrer Claude Code et donner le prompt :

```
Lis orchestration/prompt.md et exécute-le.
```

### Suivre l'avancement

- Backlog et dépendances : `orchestration/manifest.yaml`
- Statuts courants : `C:\Source\liakont-orchestration\state.yaml`
- Historique : `C:\Source\liakont-orchestration\events.jsonl` et `session-log/`

## Documents clés

| Document | Rôle |
|---|---|
| `blueprint.md` | Doctrine d'architecture du produit |
| `docs/conception/README-Index-Conception.md` | Index des specs fonctionnelles F01-F12 |
| `docs/market/Conception-Produit-Passerelle.md` | Vision produit et décisions structurantes |
| `docs/market/SYNTHESE-DR-Commerciales.md` | Synthèse des analyses marché/commercial |
| `orchestration/manifest.yaml` | Backlog de développement |
