# Conformat

**Passerelle de conformité facturation électronique pour logiciels métier legacy.**

Conformat est une Solution Compatible (SC) qui permet à un logiciel métier legacy
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
  conception/           — Specs fonctionnelles F01-F11 (source de vérité produit)
  market/               — Analyse d'opportunité, marché, offre commerciale (DR7-DR17)
  architecture/         — Conventions du repo (à produire : SOL03)
  adr/                  — Architecture Decision Records
tools/                  — Scripts de vérification et d'orchestration
tasks/                  — Plans de travail, leçons apprises
src/                    — Code source (à produire — voir blueprint.md pour la structure cible)
tests/                  — Tests (à produire)
```

## Architecture cible (résumé)

```
Gateway.sln
├─ Gateway.Core/                    ★ LE PRODUIT (générique, réutilisable)
│  ├─ Pivot/                          Modèle pivot EN 16931 + contrat IExtractor
│  ├─ TvaMapping/                     Moteur de mapping TVA (table externe validée)
│  ├─ Validation/                     Contrôles qualité pré-envoi (~20 règles)
│  ├─ Tracking/                       SQLite : anti-doublons, états, piste d'audit 10 ans
│  ├─ PaClient/                       Abstraction PA + client B2Brouter
│  ├─ Pipeline/                       extract → check → send → sync (host-agnostique)
│  └─ Configuration/                  Config JSON + secrets DPAPI
├─ Gateway.Adapters.EncheresV6/     ★ Premier adaptateur (Magic XPA / Pervasive — CMP)
├─ Gateway.App/                       Console admin WPF (opérateur comptable)
├─ Gateway.Cli/                       Mode automatique (tâche planifiée)
└─ Gateway.Service/                   Service Windows (ordonnanceur interne)
```

Stack : **.NET Framework 4.8** (compatibilité Windows legacy), WPF, SQLite, Newtonsoft.Json.
Déploiement : **on-premise uniquement**. Voir `blueprint.md` pour la doctrine complète.

## Orchestration multi-agents

Le développement est piloté par un système d'orchestration multi-agents (principe Stratum) :

- **Ce dépôt** contient le backlog, les blueprints d'exécution et le code.
- **`C:\Source\conformat-orchestration`** (dépôt séparé, `$ORCH_REPO`) contient l'état
  runtime : statuts des items, leases de slots, journal d'événements, logs de sessions.
- Chaque agent Claude Code tourne dans son propre clone (`Conformat`, `Conformat2`, ...),
  réclame un slot, prend un item éligible, l'implémente, le vérifie, le fait reviewer,
  le merge et libère son slot.

### Lancer une session d'orchestration

Dans un clone du dépôt, démarrer Claude Code et donner le prompt :

```
Lis orchestration/prompt.md et exécute-le.
```

### Suivre l'avancement

- Backlog et dépendances : `orchestration/manifest.yaml`
- Statuts courants : `C:\Source\conformat-orchestration\state.yaml`
- Historique : `C:\Source\conformat-orchestration\events.jsonl` et `session-log/`

## Documents clés

| Document | Rôle |
|---|---|
| `blueprint.md` | Doctrine d'architecture du produit |
| `docs/conception/README-Index-Conception.md` | Index des specs fonctionnelles F01-F11 |
| `docs/market/Conception-Produit-Passerelle.md` | Vision produit et décisions structurantes |
| `docs/market/SYNTHESE-DR-Commerciales.md` | Synthèse des analyses marché/commercial |
| `orchestration/manifest.yaml` | Backlog de développement |
