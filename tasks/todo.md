# PIP01d — SYNC + point de statut agent + affinage dédoublonnage (ADR-0012/0014)

Dernier maillon du pipeline cœur. Branche `feat/pipeline-PIP01d` (segment `feat/pipeline`).
Session : orch-20260606-020510-l1s1 (slot-1, clone Liakont).

## Partie A — SYNC (job planifié par tenant)
- [ ] `Contracts/Jobs/SyncAllTrigger.cs` (record, déclencheur système, miroir SendAllTrigger)
- [ ] `Infrastructure/Sync/SyncTenantJob.cs` (ITenantJob ; par Issued : facture PA si SupportsDocumentRetrieval, tax reports si SupportsTaxReportRetrieval ; addenda WORM idempotents ; RunLog(Sync))
- [ ] `Infrastructure/Sync/SyncAllFanOutHandler.cs` (IJobHandler<SyncAllTrigger>, fan-out ITenantJobRunner)
- [ ] `Infrastructure/Sync/SyncTally.cs` + `SyncOutcome.cs` (compteurs)
- [ ] Enregistrement DI dans `PipelineModuleRegistration`
- Attribution tax-report PAR DOCUMENT : `GetDocumentStatusAsync(paDocId).TaxReportIds` ∩ `ListTaxReportsAsync()` (jamais d'invention)

## Partie B — Point de statut agent (GET /api/agent/v1/documents/status)
- [ ] `Contracts/Queries/GetDocumentIntakeStatusQuery.cs` (IRequest<DocumentStatusResultDto>)
- [ ] `Infrastructure/Status/GetDocumentIntakeStatusHandler.cs` (null→Pending, présent→Processed ; tenant-scopé)
- [ ] Endpoint Host `AgentApiEndpoints.cs` : GET documents/status → 200+Pending pour clé inconnue (JAMAIS 404)

## Partie C — Affinage dédoublonnage (ADR-0012)
- [ ] `IDocumentIntake` (Ingestion.Contracts) : + `IsDocumentRangedAsync(documentId)`
- [ ] `DocumentIntake` (Documents) : impl (existence par id)
- [ ] `NoOpDocumentIntake` : retourne true (rien à ranger)
- [ ] `IReceivedDocumentUnitOfWork` : + `GetDocumentIdByPayloadHashAsync`
- [ ] `PostgresReceivedDocumentUnitOfWork` : impl
- [ ] `IngestDocumentBatchHandler` : duplicate « reçu non rangé » → re-stage + re-range (idempotent)
- [ ] Doubles de test Ingestion (UoW + intake)

## Partie D — Fake PA (pour tester SYNC tax reports)
- [ ] `FakePaClientOptions` : + `TaxReports`, `IssuedTaxReportIds` (défaut vides)
- [ ] `FakePaClient` : ListTaxReportsAsync/GetTaxReportAsync/GetDocumentStatusAsync.TaxReportIds reflètent les options

## Partie E — Tests
- [ ] SyncTenantJob unit (capacités on/off)
- [ ] SYNC intégration (Testcontainers : facture PA + tax report archivés ; sans capacité = rien)
- [ ] Status handler unit (Pending/Processed)
- [ ] Dédoublonnage intégration (intake échoué → renvoi = rangé)
- [ ] E2E (golden contrat-v1 → ingestion → CHECK → SEND Fake → SYNC → archive, 2 tenants)
- [ ] INVARIANTS/SCENARIOS Pipeline (SYNC/statut/dédoublonnage)

## Vérification
- [ ] verify-fast (net10 + agent net48)
- [ ] run-tests
- [ ] codex-review propre
