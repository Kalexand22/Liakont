# API02a — Endpoints d'action documents : envoi (ADR-0016)

Branche : `feat/console-web-API02a` (segment `feat/console-web`). Slot 2.

## Objet
Câbler les 3 actions d'envoi de la console sur le harness API01a, **tenant-scopées** (ADR-0016) :
- `POST /api/v1/documents/{id}/send` — envoi d'un document ReadyToSend (404/409, journalisé).
- `POST /api/v1/documents/send-all` — envoi des ReadyToSend du tenant COURANT ; `?confirm=false` =
  récapitulatif (nombre + montant total `decimal`) sans exécuter ; `confirm=true` = exécution.
- `POST /api/v1/runs/trigger` — déclenchement manuel du pipeline du tenant courant (`?dryRun=`).
Permission `liakont.actions`. Toute action journalisée (Audit + identité opérateur).

## Décision d'architecture (ADR-0016 — rien d'inventé)
- Nouveau déclencheur **mono-tenant** `SendTenantTrigger(string TenantId, bool DryRun)` (Pipeline.Contracts.Jobs).
  JAMAIS `SendAllTrigger` (fan-out tous-tenants = fuite cross-tenant, réservé au cron).
- Handler système `SendTenantFanInHandler : IJobHandler<SendTenantTrigger>` : `ITenantScopeFactory.Create(payload.TenantId)`
  → `SendTenantJob(PipelineRunTrigger.Manual, payload.DryRun).ExecuteAsync(...)`. Pas de `RunForAllTenantsAsync`.
- Publication HTTP→worker sur la **queue SYSTÈME** : enqueue dans un scope null-tenant frais
  (`IServiceScopeFactory.CreateAsyncScope()` → `IConnectionFactory` null-tenant → DB système), comme le scheduler.
  JAMAIS `IJobQueue` en scope HTTP tenant (= job orphelin non consommé). Tenant cible porté dans la charge utile.
- Découverte du handler par le worker : `AddJobHandler<SendTenantTrigger, SendTenantFanInHandler>()` dans le Host
  (compose handler + `JobHandlerRegistration`), comme `DailyAnchoringTrigger` (les triggers fan-out Pipeline
  existants n'ont pas encore de registration de découverte — non publiés à ce jour).

## Fichiers
Production :
- [ ] CREATE `src/Modules/Pipeline/Contracts/Jobs/SendTenantTrigger.cs`
- [ ] CREATE `src/Modules/Pipeline/Infrastructure/Send/SendTenantFanInHandler.cs`
- [ ] CREATE `src/Modules/Documents/Web/DocumentActionsEndpointMapping.cs` (send + send-all)
- [ ] MODIFY `src/Modules/Pipeline/Web/PipelineEndpointMapping.cs` (+ POST /runs/trigger)
- [ ] MODIFY `src/Host/Liakont.Host/Startup/AppBootstrap.cs` (AddJobHandler + MapDocumentActionsEndpoints)
- [ ] MODIFY `src/Modules/Documents/Web/*.csproj` (+ Pipeline.Contracts, Job.Contracts)
- [ ] MODIFY `src/Modules/Pipeline/Web/*.csproj` (+ Job.Contracts)

Tests (anti faux-vert ADR-0016) :
- [ ] MODIFY `ConsoleApiFactory.cs` — user opérateur (liakont.actions) ADDITIF + helpers (system/tenant job count, audit, run_log)
- [ ] CREATE `DocumentActionsEndpointsIntegrationTests.cs` — 202/403/404/409, recap confirm=false/true, audit,
      publication queue SYSTÈME (pas d'orphelin tenant, pas de SendAllTrigger)
- [ ] CREATE `RunsTriggerEndpointsIntegrationTests.cs` — 202/403, dryRun, publication système
- [ ] CREATE `SendTenantTriggerHandlerIntegrationTests.cs` — exécution réelle tenant-scopée (run_log Manual/Send dans A, AUCUN dans B)

## Invariants couverts
INV-API02a-1 (mono-tenant), -2 (queue système, pas d'orphelin), -3 (ITenantScopeFactory), -4 (pas de fan-out), -5 (send-all tenant courant).

## Vérification
- [x] verify-fast (plateforme .NET 10 + agent net48) — PASS
- [x] run-tests (suite complète) — PASS (3970 tests, 0 échec ; mes tests d'intégration tournent contre PostgreSQL réel)
- [ ] codex-review -Base feat/console-web (boucle jusqu'à clean)

## Résultats
- Tous les fichiers prévus créés/modifiés (production + tests). `SendTenantTrigger` + `SendTenantFanInHandler`
  enregistrés via `AddJobHandler` (Host) → découvrables par le JobWorker.
- Publication corrigée vs l'attaque morte antérieure (qui publiait `SendAllTrigger` via `IJobQueue` en scope
  HTTP tenant = orphelin + fan-out cross-tenant). Branche stale préservée : `feat/console-web-API02a-stale-dead-session`.
- Tests d'intégration anti faux-vert : publication sur la queue SYSTÈME (delta +1), AUCUN job orphelin en base
  tenant, AUCUN `SendAllTrigger`, audit awaité, et EXÉCUTION réelle tenant-scopée prouvée par `run_log`
  (run_type=Send/run_trigger=Manual) dans le tenant cible et ZÉRO chez l'autre tenant.
- Décision : `/documents/{id}/send` valide l'état (404/409) puis publie le SEND tenant-scopé (le SendTenantJob
  émet les ReadyToSend, dont ce document) — conforme ADR-0016 « le handler exécute SendTenantJob », pas de
  chemin d'envoi mono-document inventé (CLAUDE.md n°2/3).
