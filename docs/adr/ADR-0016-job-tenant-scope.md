# ADR-0016 — Scope tenant d'un job déclenché depuis la console (action HTTP → worker)

- **Statut** : Proposé (2026-06-08).
- **Date** : 2026-06-08
- **Contexte décisionnel** : `orchestration/items/API.yaml` (API02a, bloqué le 2026-06-07),
  `session-log/orch-20260607T215354Z-kalexand-slot2_API02a.md` (diagnostic sur pièce),
  `CLAUDE.md` n°9 (toute requête métier tenant-scopée), `blueprint.md` §7 (multi-tenancy),
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (mécanique `ITenantJob`/`TenantJobRunner`,
  SOL06), `docs/architecture/tenant-jobs.md`, `docs/conception/F10` (actions de la console),
  `docs/conception/F11` (ordonnancement : module Job côté système), `docs/adr/socle/ADR-0011-database-per-tenant.md`.

## Contexte

API02a câble les actions d'envoi de la console (`POST /documents/{id}/send`,
`POST /documents/send-all`, `POST /runs/trigger`). L'implémentation a été **bloquée en review
d'intégration** : non pas un bug ponctuel, mais l'absence d'un contrat tranché sur le **scope tenant
d'un job déclenché depuis HTTP**. Deux défauts structurels (tous deux constatés sur pièce dans le
session-log) :

1. **Job orphelin (HTTP → worker)** — En database-per-tenant (ADR-0011 socle), une action console
   s'exécute dans un scope HTTP **tenant-résolu**. Publier le job d'envoi via la chaîne
   `IJobQueue`/`PostgresJobQueue` l'écrit dans `job.jobs` de la base **DU tenant**. Or le `JobWorker`
   (BackgroundService) tourne **null-tenant** et ne sonde que `job.jobs` de la base **système** : le
   job publié en base tenant n'est **jamais consommé**. L'opérateur reçoit `202 { jobId }`, mais rien
   ne part — un **no-op silencieux** (faux-vert : les tests n'assertaient que le `202`, jamais
   l'exécution réelle). Le pattern correct existe déjà pour les jobs planifiés (`JobScheduler` insère
   le job en base système avec une colonne `companyId`, et les handlers de fan-out font le fan-out via
   `ITenantJobRunner`) : la queue d'exécution réelle est la queue **système**, jamais une queue per-tenant.

2. **Fan-out cross-tenant depuis une action mono-opérateur** — Le seul déclencheur d'envoi existant,
   `SendAllTrigger`, est traité par un handler de fan-out → `ITenantJobRunner.RunForAllTenantsAsync`
   qui exécute le SEND pour **TOUS les tenants actifs** (`ITenantQueries.ListAsync`, sans filtrer par
   `company_id`). Câbler l'action `send-all` d'UN opérateur sur ce déclencheur ferait transmettre à
   l'administration fiscale les documents des tenants B, C… depuis l'action du tenant A. C'est une
   **fuite cross-tenant** : violation directe de `CLAUDE.md` n°9 et `blueprint.md` §7 (seul le module
   Supervision est cross-tenant, en lecture seule). Le fan-out tous-tenants est légitime **uniquement**
   pour le déclencheur planifié (cron = geste opérateur d'instance), pas pour une action de console.

Trancher le scope tenant de ces jobs est une **décision d'architecture** (isolation tenant = risque
fiscal client dans un produit de conformité), pas un correctif d'un item de câblage d'endpoints.
`CLAUDE.md` n°9 ne laisse pas de choix ouvert : la décision ci-dessous **applique** la règle, elle ne
l'arbitre pas.

## Décision

### 1. Un job déclenché par une action de console est TENANT-SCOPÉ (un seul tenant cible)

Une action de console agit pour le **seul tenant de l'opérateur**. On introduit donc un déclencheur
d'envoi **mono-tenant** portant explicitement le tenant cible, distinct du déclencheur planifié
tous-tenants existant :

- **`SendTenantTrigger(string TenantId, bool DryRun)`** (Pipeline.Contracts.Jobs) — charge utile d'un
  job dont le handler exécute le SEND pour **le seul** `payload.TenantId`, en réutilisant l'unique
  mécanisme sanctionné de basculement de tenant : `ITenantScopeFactory.Create(tenantId)` (SOL06,
  ADR-0006), puis exécution de `SendTenantJob` (déjà `ITenantJob`) dans ce scope. Le runner
  tous-tenants (`RunForAllTenantsAsync`) n'est **PAS** appelé : on cible un tenant, on n'itère pas.
- Le déclencheur planifié tous-tenants **reste** `SendAllTrigger` — son fan-out est légitime car
  porté par le cron d'instance, jamais par une action d'opérateur.
- `runs/trigger` suit la même règle : déclenchement **manuel** (`PipelineRunTrigger.Manual`)
  tenant-scopé sur le tenant de l'opérateur.

### 2. La publication HTTP → worker passe par la queue SYSTÈME (jamais la queue tenant)

L'endpoint **ne publie pas** le job dans la base du tenant. Il le publie sur la queue **système**
(celle que le `JobWorker` consomme réellement), exactement comme le `JobScheduler` pour les jobs
planifiés (`JobEntry.Create(..., companyId:)` en base système). Le tenant cible est porté **dans la
charge utile** (`SendTenantTrigger.TenantId`, renseigné depuis `actor.TenantId`), pas par le routage
de connexion de la queue. Le handler système rétablit ce tenant via `ITenantScopeFactory.Create(...)`
au moment de l'exécution. Aucune nouvelle abstraction de scope système n'est créée : on réutilise le
chemin de publication système déjà emprunté par les déclencheurs planifiés.

### 3. `send-all` opère sur le tenant COURANT uniquement

`POST /documents/send-all` envoie les `ReadyToSend` du **tenant de l'opérateur** (récapitulatif puis,
sur `confirm=true`, publication d'un `SendTenantTrigger(actor.TenantId, …)`). **Aucun** fan-out
cross-tenant, **aucune** énumération de `ITenantQueries.ListAsync` depuis un chemin d'action de
console. Le récapitulatif (nombre + montant total en `decimal`) est calculé sur le seul tenant
courant.

## Invariants

- **INV-API02a-1** — Un job déclenché par une action de console ne s'exécute QUE pour le tenant de
  l'opérateur (`actor.TenantId`). Jamais d'itération multi-tenant depuis un chemin d'action de
  console.
- **INV-API02a-2** — La publication d'un job depuis HTTP se fait sur la queue **système** (consommée
  par le `JobWorker`), jamais dans `job.jobs` de la base tenant. Pas de job orphelin.
- **INV-API02a-3** — Le basculement vers le tenant cible passe exclusivement par
  `ITenantScopeFactory.Create(tenantId)` (le seul établisseur de tenant sanctionné hors middleware,
  ADR-0006). Aucune couche métier ne mute le contexte de tenant.
- **INV-API02a-4** — `RunForAllTenantsAsync` (fan-out tous-tenants) n'est invoqué QUE depuis un
  déclencheur planifié d'instance (cron), jamais depuis un chemin d'action de console.
- **INV-API02a-5** — `send-all` ne lit et n'envoie QUE les `ReadyToSend` du tenant courant ; le
  récapitulatif est calculé sur ce seul tenant.

## Conséquences

**Positif** : l'envoi déclenché depuis la console s'exécute réellement (le job est consommé par le
worker système) ; l'isolation tenant de `CLAUDE.md` n°9 est garantie par construction (le tenant
cible est explicite et unique) ; on réutilise intégralement la mécanique SOL06 (ADR-0006) et le
chemin de publication système des jobs planifiés — aucun fichier `Stratum.*` vendored modifié, aucune
nouvelle abstraction inventée ; les tests peuvent prouver l'**exécution réelle** (run_log écrit dans
la base du tenant cible, et **aucune** trace chez les autres tenants), anti-faux-vert.

**À la charge d'API02a** : ajouter `SendTenantTrigger` + son handler système (qui appelle
`ITenantScopeFactory.Create` puis `SendTenantJob.ExecuteAsync`), faire publier les endpoints sur la
queue système avec `actor.TenantId` dans la charge utile, et asserter l'isolation cross-tenant en
test d'intégration (au moins 2 bases tenant).

**Limite** : la garantie « un seul job send/sync par tenant à la fois » reste portée par
l'ordonnanceur / le worker (comportement existant), pas introduite ici.

## Alternatives rejetées

- **Câbler `send-all` sur `SendAllTrigger` (fan-out tous-tenants)** : transmet au fisc les documents
  d'autres tenants depuis l'action d'un opérateur — fuite cross-tenant, P1 `CLAUDE.md` n°9. **Rejetée.**
- **Publier le job dans la base du tenant (`IJobQueue` en scope HTTP tenant)** : le `JobWorker`
  null-tenant ne le consomme jamais → no-op silencieux (job orphelin). **Rejetée.**
- **Faire consommer une connexion système par la chaîne UoW du module Job** : touche du code
  `Stratum.*` vendored (provenance, `CLAUDE.md` n°11/20) pour un besoin déjà couvert par le chemin de
  publication système existant. **Rejetée** (inutile et plus risqué).

## Références

- `CLAUDE.md` n°9 (requêtes tenant-scopées) ; `blueprint.md` §7 (multi-tenancy)
- `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06 : `ITenantJob`/`TenantJobRunner`/`ITenantScopeFactory`)
- `docs/architecture/tenant-jobs.md` ; `docs/adr/socle/ADR-0011-database-per-tenant.md`
- `docs/conception/F10` (actions de la console) ; `docs/conception/F11` (ordonnancement, module Job système)
- `orchestration/items/API.yaml` (API02a) ; `session-log/orch-20260607T215354Z-kalexand-slot2_API02a.md`
