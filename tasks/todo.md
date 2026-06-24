# Plan B4+B6+A6 — JOB orchestrateur e-reporting B2C marge (enchères, flux 10.3)

> Branche `feat/ereporting-b2c`. Suite de la recette (existant VERT : verify-fast + 27 tests fiscaux
> + envoi réel sandbox id 591). Cartographie scaffolding faite (7 lecteurs). Contrats ci-dessous = VÉRIFIÉS.

## 0. État recette (fait)
- [x] verify-fast 3 solutions VERT
- [x] Tests fiscaux 27/0 (Pipeline 17 / Transmission 4 / SuperPdp 6)
- [x] Envoi RÉEL sandbox `POST /b2c_transactions` (id 591, Issued) — test gardé `[Trait("Category","Sandbox")]` (non committé)

## 1. Contrats confirmés (cartographie)
- `ITenantJob { string Name; Task ExecuteAsync(TenantJobContext ctx, ct) }` ; `TenantJobContext { TenantId; Services (DI tenant-routé) }` ; **jamais de boucle tenant** (le runner fait le fan-out, ADR-0006).
- Fan-out : `Trigger` → `IJobHandler<Trigger>` (FanOutHandler) → `ITenantJobRunner.RunForAllTenantsAsync(job, ct)`. Enregistrement `AddJobHandler<Trigger,Handler>("libellé")` dans `PipelineSystemJobHandlers.cs`. Runner enregistré `AppBootstrap.cs:169` (`AddTenantJobs`).
- Découverte docs : `IDocumentQueries.GetDocumentsAsync(DocumentListFilter{From,To,State,Type,Search,Page,PageSize}, ct)` → `DocumentListResult` (paginé, tenant-scopé).
- Pivot stagé : `IPayloadStagingStore.ReadAsync(StagedPayloadKey(tenantId, documentId, payloadHash), ct)` → JSON canonique (hash re-vérifié) ; `PivotCanonicalJsonReader.Read(json)` → `PivotDocumentDto`.
- Marqueur `IsB2cReportingDeclaration` = bool dans le **pivot JSON stagé** (PAS de colonne SQL), défaut false, hash-neutre. Frais : `PivotSellerFeeDto/PivotBuyerFeeDto { LotReference; NetAmount (decimal TTC); SourceRegimeCode (BRUT); SourceLineRef; Description }`.
- Mapping TVA : `ITvaMappingService.MapAsync(Guid companyId, IReadOnlyList<TvaLineMappingRequest>, ct)` → `DocumentTvaMappingResult{TableExists,IsValidated,MappingVersion,Lines[]}` ; `TvaLineMappingRequest{SourceRegimeCode, Part, SourceFlags?, LineRef?}` ; `TvaMappingPart{Adjudication=0,Frais=1,Autre=2}` ; `TvaLineMappingResult{IsMapped, Category?, Rate? (decimal, null=non mappé), Vatex?, BlockReason?}`.
- Domaine PUR (déjà livré) : `B2cResolvedHonoraire{AmountTtc, RatePercent?}` → `B2cMarginResolver.Resolve(bool documentHasSeparateVat, honoraires[])` → `B2cMarginResolution{IsResolved,MarginTtc,RatePercent,BlockReason?}` (fail-closed `SeparateVat/NoHonoraires/UnmappedRate/MixedRates`) → `B2cMarginContribution{DocumentId,SourceReference,Date,CurrencyCode,MarginTtc,RatePercent}` → `B2cTransactionAggregationCalculator.Aggregate(...)` → `B2cAggregatedTransaction{Date,CurrencyCode,TaxExclusiveAmount,TaxTotal,Subtotals[],Contributions[]}`.
- Transport (déjà livré) : map `B2cAggregatedTransaction` → `B2cReportingTransaction{Category=Tma1, Role=Seller, CurrencyCode, Date, TaxExclusiveAmount, TaxTotal, Subtotals(TaxPercent/TaxableAmount/TaxTotal)}` → `IPaClient.SendB2cTransactionAsync(tx, ct)` (gardé capacité ; SuperPdp surcharge = envoi réel).
- Traçabilité : `IReportingPieceLinkStore` (Archive) `AppendAsync(companyId, documentId, sourceReferences[], ct)` / `GetByDocumentAsync(companyId, documentId)` / `GetBySourceReferenceAsync`. Append-only (V011), idempotent `UNIQUE(company_id,document_id,source_reference)`. Consommateur export : `FiscalControlExportService.BuildReportingLinkFileAsync` → `GetByDocumentAsync`.
- Patrons store/migration : migrations `Pipeline/Infrastructure/Migrations/Vxxx__*.sql` (DbUp). Actuel V001–V005. **Append-only V005** (report_rectifications : triggers REJECT UPDATE/DELETE/TRUNCATE, seq IDENTITY, jsonb) = patron de l'état d'émission. Stores : interface `Application/`, impl Dapper `Infrastructure/Persistence/`, DI `PipelineModuleRegistration.AddPipelineModule()`.
- Trace : `RunLog.Start(PipelineRunType, PipelineRunTrigger, utc)/.Complete(...)` → `IPipelineRunLogStore.SaveAsync`. `PipelineRunType{Check=0,Send=1,Sync=2,Aggregate=3,Rectify=4}` → ajouter `B2cMarginAggregate=5`.

## 2. Constat clé — DEUX flux B2C distincts (à confirmer Karl)
- **Taxable** (existant) : doc `IsB2cReportingDeclaration` + ligne taxable (TVA) + **sans frais** → `SendTenantJob` → `SendDocumentAsync` (par-document). Fixtures/tests existants = ce cas.
- **Marge** (B4, nouveau) : doc `IsB2cReportingDeclaration` + `SellerFees/BuyerFees` + **sans TVA distincte** (art. 297 E) → agrégat N→1 → `SendB2cTransactionAsync` (TMA1/SE).
- **Régression latente** : SuperPdp ayant `SupportsB2cReporting=true`, un doc marge qui atteint le chemin par-document part en `SendDocumentAsync` → **rejet SuperPDP** (pas de SIREN acheteur). → `SendTenantJob` doit **tenir** les docs marge hors du par-document (nouvelle garde, jamais affaiblir l'existante).

## 3. Décisions TRANCHÉES (Karl, sign-off)
- [x] **D1 — Discrimination & garde SendTenantJob.** Marge (frais, pas de TVA distincte) → job agrégé ; taxable (ligne TVA, pas de frais) → par-document inchangé. **Nouvelle garde dans `SendTenantJob` qui TIENT les docs marge hors du par-document** (jamais affaiblir l'existante).
- [x] **D2 — Traçabilité B6.** **Garder la clé document** (`company_id, document_id, source_reference`) du `ReportingPieceLink` (export `GetByDocumentAsync` préservé) ; déplacer le GEL **après confirmation d'envoi** de l'agrégat (un lien par contribution). PAS de re-clé sur l'agrégat.
- [x] **D3 — Idempotence PAR DOCUMENT (attempt-once).** L'état d'émission est suivi **par document marge**. Le job agrège les docs SANS enregistrement d'émission ; doc tardif sur jour déjà émis → **nouvel agrégat** (SuperPDP additionne côté serveur). Crash-safe : enregistrement `Pending` **avant** le POST → exclusion au run suivant même en cas de crash (jamais 2 POST). Issue non-`Issued` (Rejected/Technical/Pending orphelin) = **signalée opérateur**, jamais re-tentée en auto (l'API n'a aucun dédoublonnage).

## 4. Implémentation (après sign-off) — Lot B4
- [ ] `IB2cMarginEmissionStore` (Application) : `GetByAggregateAsync(key, ct)` + `AppendAsync(entry, ct)` (append-only).
- [ ] `B2cMarginEmissionEntry` + `B2cMarginEmissionStatus{Issued,RejectedByPa,TechnicalError}` ; clé déterministe `AggregateId = (companyId, date, currency, TMA1, SE)`.
- [ ] Migration **V006** `create_b2c_margin_emissions` (miroir V005 : append-only triggers, seq IDENTITY, `numeric(18,2)`, jsonb snapshot, `pa_emission_id text`). PAS de réutilisation de `payment_aggregations`.
- [ ] `PostgresB2cMarginEmissionStore` (Dapper) + DI `PipelineModuleRegistration`.
- [ ] `B2cMarginAggregatorTenantJob : ITenantJob` (`Name="pipeline.aggregate-b2c-margin"`) : découverte (GetDocumentsAsync + read staged pivot, filtre marqueur+frais) → MapAsync(Part.Frais) → B2cMarginResolver → contributions → Aggregate → pour chaque agrégat : clé déterministe → si déjà Issued (store) skip → SendB2cTransactionAsync → AppendAsync emission → (D2) freeze liens par contribution → RunLog.
- [ ] `PipelineRunType.B2cMarginAggregate=5`.
- [ ] Trigger `AggregateB2cMarginAllTrigger` + `AggregateB2cMarginAllFanOutHandler` + `AddJobHandler<...>` (PipelineSystemJobHandlers).
- [ ] Garde D1 dans `SendTenantJob` (HOLD docs marge du par-document) + log opérateur FR.

## 5. Lot B6 (traçabilité) + A6 (provisioning)
- [ ] B6 : gel des liens après confirmation d'envoi de l'agrégat (clé document conservée) ; test d'intégration réversibilité N→1 (l'export retrouve les liens) ; réécrire tests B2C04 si besoin.
- [ ] A6 : provisioning `company.vat_regime` via `PATCH /companies` (param tenant ; `null` → suspendre, jamais deviné).

## 6. Vérification (avant push)
- [ ] verify-fast 3 solutions + build **Release** des projets touchés (StyleCop gâtée Release).
- [ ] run-tests (Testcontainers) : store/migration/idempotence (rejeu = pas de re-POST) + réversibilité traçabilité + garde D1.
- [ ] Tests unitaires du job (fakes) : discrimination, fail-closed, agrégation, skip-si-émis.
- [ ] e2e sandbox gardé (déjà : id 591).
- [ ] codex-review boucle propre. Merge main = humain (Karl).
