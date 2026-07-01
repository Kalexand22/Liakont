# GED10 — Backfill rétroactif idempotent + démo enchères/BTP (généricité par config)

Segment `ged` (feat/ged), sous-branche `feat/ged-GED10`. Blueprint `module-work-item`.
Spec F19 §10/§11 D12, RL-16/RL-21. Deps GED05b + GED07 (done).

## Contraintes de frontière (déterminées par exploration — NON négociables)
- **GED est un silo** : `GedBoundaryTests` interdit toute dépendance de `Ged.*` vers
  `Liakont.Modules.{Pipeline,Validation,Transmission,Documents}` (IL). ⇒ GED ne lit JAMAIS
  `Documents.*` (ni Contracts).
- **Archive n'a pas le droit de référencer Ged** : `ArchiveBoundaryTests` interdit
  `Archive → Liakont.Modules.Ged`.
- **GED11 lint (à venir)** : SQL GED reste dans les schémas `ged_*` (pas de `documents.`).
- ⇒ L'orchestration cross-module (lire l'archive fiscale + le doc fiscal, puis écrire l'index GED)
  vit dans le **Host** (racine de composition, voit tout). GED n'expose qu'un point d'entrée
  d'indexation via **Ged.Contracts**.

## A. Indexeur GED partagé (extraction depuis le consommateur GED05b)
- [ ] `Ged.Application/Index/IGedDocumentIndexer.cs` — `IndexAsync(GedIndexRequest) → GedIndexOutcome`
      + `IndexDeferredAsync(managedDocId, sourceRef, docKind, reason)`.
- [ ] `Ged.Application/Index/GedIndexRequest.cs` — { Guid ManagedDocumentId, IngestedDocumentDto Ingested,
      string Source, GedDocumentSoftLinks? SoftLinks }.
- [ ] `Ged.Application/Index/GedDocumentSoftLinks.cs` — { Guid? FiscalDocumentId, Guid? ArchiveEntryId,
      string? ArchivePath, string? ContentHash }.
- [ ] `Ged.Infrastructure/Index/GedDocumentIndexer.cs` — logique map→valide→écrit (indexed/deferred) via UoW,
      soft-links, source paramétrable (extraite du consommateur).
- [ ] Refactor `ManagedDocumentReceivedConsumer` → délègue à `IGedDocumentIndexer` (reste : scope + staging).
- [ ] Étendre `ManagedDocument` (Domain) avec soft-links optionnels (nullable, défaut null — additif).
- [ ] Étendre `UpsertManagedDocumentAsync` SQL (PostgresGedIndexUnitOfWork) pour insérer les soft-links.
- [ ] DI : `AddScoped<IGedDocumentIndexer, GedDocumentIndexer>`.

## B. Point d'entrée backfill GED (Ged.Contracts + Infra)
- [ ] `Ged.Contracts/Backfill/IGedArchivedDocumentBackfill.cs` — `BackfillAsync(GedBackfillDocumentRequest) → GedBackfillOutcome`.
- [ ] `Ged.Contracts/Backfill/GedBackfillDocumentRequest.cs` — { ArchiveEntryId, FiscalDocumentId, ArchivePath,
      ContentHash, DocumentType, SourceReference, SourceFields (dict), SourceTimestampUtc? }.
- [ ] `Ged.Contracts/Backfill/GedBackfillOutcome.cs` — enum { Indexed, Deferred, AlreadyPresent }.
- [ ] `Ged.Infrastructure/Backfill/GedArchivedDocumentBackfill.cs` — id déterministe (GUIDv5 depuis ArchiveEntryId),
      construit IngestedDocumentDto, appelle l'indexeur (source="import", soft-links). Idempotent (ON CONFLICT id).
- [ ] DI : `AddScoped<IGedArchivedDocumentBackfill, GedArchivedDocumentBackfill>`.

## C. Job backfill (Host — orchestration cross-module)
- [ ] `Host/.../GedCorpusBackfillTrigger.cs` — marqueur.
- [ ] `Host/.../GedCorpusBackfillTenantJob.cs : ITenantJob` — enumère `IArchiveEntryStore.GetChainAsync`
      → par entrée : `IDocumentQueries.GetByIdAsync` → construit GedBackfillDocumentRequest → BackfillAsync.
      GetChainAsync est déjà un instantané complet. Compte les issues.
- [ ] `Host/.../GedCorpusBackfillFanOutHandler.cs : IJobHandler<GedCorpusBackfillTrigger>` — fan-out via runner.
- [ ] `AppBootstrap` : `AddJobHandler<GedCorpusBackfillTrigger, GedCorpusBackfillFanOutHandler>`.
- [ ] `SystemJobDefinitions` : entrée DeploymentCadence (CronExpression null, geste opéré).

## D. Seeds démo (deployments/) — FICTIFS (n°7)
- [ ] `deployments/demo-encheres-ged/ged-catalog.sql` — entity_types + axis_definitions (numero_lot multi,
      numero_vente, entité acheteur) + ged_mapping_profiles (+ change-log).
- [ ] `deployments/demo-btp-ged/ged-catalog.sql` — axis_definitions (numero_situation, mois,
      montant_ht_cumule number scale=2 EUR, avancement_pct number scale=0) + entité chantier + profil.
- [ ] Vocabulaire métier UNIQUEMENT ici + tests, JAMAIS dans src/Modules/Ged/**.

## E. Tests
- [ ] `Ged.Tests.Integration/GedArchivedDocumentBackfillIntegrationTests` — backfill idempotent (no-op au 2e
      passage) + deferred sans profil + soft-links posés.
- [ ] `Ged.Tests.Integration/GedGenericityDemoIntegrationTests` — applique les 2 seeds → indexe bordereaux
      (via indexeur) → filtre §6.2 par numero_lot → tous les docs du lot ; BTP situation (EUR + %) sur le
      MÊME schéma sans ALTER TABLE.
- [ ] `Host.Tests.Unit` — job backfill : itération + construction de requête (fakes).
- [ ] `Ged.Tests.Unit` — id déterministe, indexeur.
- [ ] Non-régression : tests GED05b (consommateur) verts après refactor.

## F. Vérif
- [ ] verify-fast (3 solutions) ; run-tests verts ; codex-review clean.
- [ ] merge-back feat/ged (attention concurrence GED08 slot-1) ; finalize.

## Review (résultats)

- **verify-fast** : PASS (3 solutions — plateforme .NET 10 + agent net48 x86/x64 + onsite).
- **run-tests** : PASS, 7573 tests, 0 échec. Les 8 tests neufs EXÉCUTÉS et verts :
  `GedDeterministicIdTests` (×2), `GedArchivedDocumentBackfillIntegrationTests` (×2, base réelle),
  `GedGenericityDemoIntegrationTests` (×2, base réelle + seeds deployments/), `GedCorpusBackfillTenantJobTests` (×2).
- **Frontières respectées** : GED ne référence PAS Documents/Archive (silo, NetArchTest) ; Archive ne référence PAS Ged
  (ArchiveBoundaryTests) ; orchestration cross-module au Host uniquement ; SQL GED reste en `ged_*` (pas de `documents.`).
- **Non-régression** : consommateur GED05b refactoré → délègue au foyer d'écriture unique ; ses tests d'intégration verts.
- codex-review : à lancer (boucle de review).
