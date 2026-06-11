# Liakont

**Passerelle de conformité facturation électronique pour logiciels métier legacy.**

Liakont est une Solution Compatible (SC) qui permet à un logiciel métier legacy
(Magic XPA/Pervasive, AS400, vieux SQL Server, Access...) de répondre aux obligations
de la réforme française de facturation électronique (e-invoicing / e-reporting,
échéances septembre 2026 et 2027) **sans modifier le logiciel source** :
extraction en lecture seule de la base, normalisation EN 16931, mapping TVA validé,
contrôles qualité, envoi vers une Plateforme Agréée (B2Brouter), piste d'audit 10 ans.

> **Périmètre V1 :** Liakont couvre l'**émission** (factures clients + avoirs) et
> l'**e-reporting**. La **réception** des factures fournisseurs n'est **pas** couverte en V1 —
> elle reste assurée par le portail de la Plateforme Agréée du client (décision 2026-06-02).

## État du projet

**Développement avancé, piloté par orchestration multi-agents.** Le cœur du produit est
implémenté : agent d'extraction (net48), pipeline d'envoi, mapping TVA paramétrable, contrôles
de validation, archivage WORM, **console web** (Blazor), supervision, plug-in PA **B2Brouter**
et adaptateur source **EncheresV6**. Les segments **outillage de déploiement**
(`deploiement-toolkit`) et **paramétrage du premier déploiement** (`deploiement-cmp`) sont en
cours. Le backlog complet et son avancement vivent dans `orchestration/manifest.yaml` et dans
le dépôt d'état `$ORCH_REPO`.

## Architecture (plateforme + agent)

Depuis le pivot du 2026-06-03, Liakont est un système à **deux composants** :

```
[Base métier client] ──ODBC lecture seule──▶ AGENT ──HTTPS push + heartbeat──▶ PLATEFORME
   (Pervasive, AS400, …)                   (net48)                            (.NET 10)
                                                                              ├─ Ingestion (contrat versionné)
                                                                              ├─ Mapping TVA (paramétrage tenant)
                                                                              ├─ Validation (~20 contrôles)
                                                                              ├─ Documents + piste d'audit
                                                                              ├─ Envoi Plateforme Agréée
                                                                              ├─ Archive WORM (10 ans)
                                                                              └─ Console web + Supervision
```

- **Plateforme** (`src/Liakont.sln`, **.NET 10**, ASP.NET Core + Blazor, PostgreSQL) : tout le
  métier, multi-tenant (un tenant = un client), la console web et la supervision cross-tenant.
- **Agent** (`agent/Liakont.Agent.sln`, **.NET Framework 4.8**, service Windows) : installé
  **chez le client**, extrait en **lecture seule** (ODBC), bufferise, pousse en HTTPS, envoie un
  heartbeat. **Aucune logique métier dans l'agent.**

Deux axes de plug-ins **symétriques** :

- **Sources** (`IExtractor`, côté agent) : EncheresV6 (Magic XPA / Pervasive)…
- **Plateformes Agréées** (`IPaClient` + `PaCapabilities`, côté plateforme) : B2Brouter (PA #1),
  Fake (démo/tests), Super PDP (à venir)…

Toute donnée d'**un** client est du **paramétrage** (`deployments/<client>/`), jamais du code ;
le comportement face à **une** PA est piloté par ses **capacités déclarées**, jamais par un
`if (pa == …)`. Voir [`blueprint.md`](blueprint.md) pour la doctrine complète et
[`docs/conception/F12-Architecture-Plateforme-Agent.md`](docs/conception/F12-Architecture-Plateforme-Agent.md)
pour l'exécution.

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
  guide-operateur.md    — Guide de l'opérateur comptable (console web)
  guide-editeur.md      — Guide de l'opérateur d'instance (éditeur / IT Innovations)
  installation-agent.md — Installation de l'agent chez le client final
  conception/           — Specs fonctionnelles F01-F14 (source de vérité produit)
  architecture/         — Conventions du repo, ADR transverses
  adr/                  — Architecture Decision Records
  market/               — Analyse d'opportunité, marché, offre commerciale
  support/              — Veille réglementaire, kit support
src/                    — Plateforme (.NET 10) : Host, Modules, Common, Contracts, PaClients
agent/                  — Agent d'extraction (.NET Framework 4.8) + adaptateurs source
deploy/docker/          — Appliance Docker Compose + sauvegarde/restauration
tools/                  — Scripts de vérification, packaging et d'orchestration
config/exemples/        — Exemples de paramétrage FICTIFS (jamais de donnée client réelle)
tasks/                  — Plans de travail, leçons apprises
```

## Construire les deux solutions

Prérequis : **.NET SDK 10** (voir `global.json`) ; pour l'agent, les **workloads de build
net48** (Windows). Le script de vérification rapide construit et teste **les deux** solutions :

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1
```

Les commandes équivalentes (extraites de `tools/verify-fast.ps1`) :

```powershell
# Plateforme (.NET 10)
dotnet restore src/Liakont.sln
dotnet build   src/Liakont.sln --no-restore
dotnet test    src/Liakont.sln --no-build --filter "Category!=Integration&Category!=Staging&Category!=Sandbox&Category!=E2E"

# Agent (.NET Framework 4.8, x86 + x64)
dotnet restore agent/Liakont.Agent.sln
dotnet build   agent/Liakont.Agent.sln --no-restore /p:Platform=x86
dotnet build   agent/Liakont.Agent.sln --no-restore /p:Platform=x64
dotnet test    agent/Liakont.Agent.sln --no-build --filter "Category!=Integration&Category!=Staging"
```

La suite complète unit + intégration s'exécute via `tools/run-tests.ps1` (les tests E2E
Playwright via `tools/run-e2e.ps1`).

## Lancer la plateforme (développement)

1. Démarrer Keycloak (OIDC) en local :
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/keycloak-dev.ps1 start
   ```
2. Démarrer l'Hôte :
   ```powershell
   dotnet run --project src/Host/Liakont.Host
   ```
   Console disponible sur `http://localhost:55996`. En environnement `Development`, un realm et
   un tenant de démonstration sont amorcés automatiquement.

Détails (Keycloak, comptes de test) : [`deploy/docker/README.md`](deploy/docker/README.md).

## Mode démonstration (sans base client, sans PA réelle)

Pour exercer le produit de bout en bout avec des données **fictives** :

1. Activer le **plug-in PA Fake** (`PaClients:Fake:Enabled`, jamais actif en production) et
   lancer la plateforme (ci-dessus).
2. Dans la console, **enregistrer un agent** (« Gestion des agents ») et copier sa clé.
3. Peupler la console avec des factures fictives via le contrat d'ingestion agent v1 :
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/dev-seed-demo-docs.ps1 -AgentKey "prefix.secret"
   ```
   (15 documents fictifs, SIREN d'exemple `123456782`, FAC/AVO, taux 20/10/5,5/0 %, B2B et B2C).
4. Suivre le parcours dans la console : mapping TVA → validation → envoi via la PA Fake.

## Déploiement (production / auto-hébergé)

L'instance se déploie comme une **appliance Docker Compose**
([`deploy/docker/appliance/`](deploy/docker/appliance/README.md)) : PostgreSQL (bases système +
tenant), Keycloak, l'Hôte Liakont, et Caddy (reverse-proxy + TLS). Sauvegarde et restauration
(`deploy/docker/backup.sh` / `restore.sh`) et procédure de réversibilité : voir le
[guide de l'éditeur](docs/guide-editeur.md).

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
| [`blueprint.md`](blueprint.md) | Doctrine d'architecture du produit |
| [`docs/guide-operateur.md`](docs/guide-operateur.md) | Guide de l'opérateur comptable (console web) |
| [`docs/guide-editeur.md`](docs/guide-editeur.md) | Guide de l'opérateur d'instance (éditeur / exploitant) |
| [`docs/installation-agent.md`](docs/installation-agent.md) | Installation de l'agent chez le client final |
| [`docs/conception/README-Index-Conception.md`](docs/conception/README-Index-Conception.md) | Index des specs fonctionnelles F01-F14 |
| [`docs/conception/F12-Architecture-Plateforme-Agent.md`](docs/conception/F12-Architecture-Plateforme-Agent.md) | Architecture plateforme + agent |
| [`docs/market/Conception-Produit-Passerelle.md`](docs/market/Conception-Produit-Passerelle.md) | Vision produit et décisions structurantes |
| [`orchestration/manifest.yaml`](orchestration/manifest.yaml) | Backlog de développement |
