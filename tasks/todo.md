# PIP03a — E-reporting de paiement : agrégation requêtable + suspension (générique, ADR-0015)

Session orchestration slot-1 (`orch-20260606-232020-slot1`), sous-branche `feat/pipeline-PIP03a`.
Blueprint module-work-item. Specs : F09, ADR-0015, ADR-0014, F12-A §3.

## Décisions de conception (verrouillées)

- **Snapshot ventilation TVA (ADR-0015)** : écrit au CHECK (PIP01b) dans une table Pipeline dédiée,
  append-only, tenant-scopée, DISTINCTE du staging (purgé) et du WORM. Ne porte que la sortie du
  mapping validé (rate ?? source rate, base HT, TVA) groupée par taux + operationCategory + mapping_version.
- **Projection d'agrégation (Pipeline)** : `pipeline.payment_aggregations` (jour×taux, statut fiscal).
  Le `Payments.PaymentAggregate` (Period déclarative obligatoire + machine à états de TRANSMISSION,
  INV-PAYMENTS-007 « état opérationnel, pas une qualification fiscale ») est l'artefact PIP03b
  (fenêtrage + envoi). PIP03a ne le touche pas. Le statut fiscal (Suspended/NotRequired/PendingCapability)
  ne peut PAS vivre sur la machine à états Payments → projection Pipeline dédiée.
- **Décomposition** : ventilation proportionnelle de l'encaissement selon la ventilation par taux
  SOURCÉE du document (F09 §2), pour les documents MONO-CATÉGORIE PrestationServices uniquement.
  Mixte → suspendu (D-b non sourcé). LivraisonBiens → non requis (pas d'exigibilité à l'encaissement).
  Arrondi commercial half-up 2 décimales. AUCUNE règle inventée.
- **FeeImputationMethod** : champ nullable de FiscalSettings (Prorata|AgregationJourTaux). null = suspension
  (jamais de prorata par défaut). Pas de workflow validated_by (D-d : nullabilité suffit).
- **AUCUN fenêtrage de période, AUCUN envoi réel** (PIP03b).

## Plan

### A. TenantSettings (CFG02) — FeeImputationMethod
- [x] enum `FeeImputationMethod` {Prorata, AgregationJourTaux} (Domain)
- [x] `FiscalSettings` : champ + Create/Reconstitute/Update
- [x] migration `V008__add_fee_imputation_method.sql`
- [x] `FiscalSettingsDto` + `PostgresTenantSettingsQueries.GetFiscalSettings`
- [x] `PostgresTenantSettingsUnitOfWork` (MapFiscal + Insert + Update)
- [x] `SetFiscalSettingsCommand` + handler + `TenantSettingsParsing.ParseFeeImputationMethod`
- [x] tests : FiscalSettingsTests, TenantSettingsParsingTests, FiscalSettingsIntegrationTests

### B. Pipeline — Snapshot ventilation (ADR-0015)
- [x] Domain : `VentilationLine`, `VentilationSnapshot`
- [x] Application : `IVentilationSnapshotStore`
- [x] migration `V003__create_ventilation_snapshots_table.sql` (append-only triggers, uq doc+version)
- [x] Infrastructure : `PostgresVentilationSnapshotStore` (jsonb, decimals exacts)
- [x] CHECK : `CheckEvaluation`/`CheckDecision` portent la ventilation ; `CheckTvaMapping.Evaluate`
      la construit ; `DocumentReceivedConsumer` écrit le snapshot (idempotent) au MarkReadyToSend
- [x] DI : enregistrer `IVentilationSnapshotStore`

### C. Pipeline — PaymentAggregator (PIP03a)
- [x] `PipelineRunType.Aggregate`
- [x] `IPaymentQueries.ListPaymentsAsync` (Payments Contracts) + Postgres impl
- [x] Domain : `PaymentAggregationStatus`, `PaymentDailyAggregate`, `PaymentAggregationCalculator` (pur)
- [x] migration `V004__create_payment_aggregations_table.sql` (projection upsert jour×taux)
- [x] Application : `IPaymentAggregationStore` ; Infrastructure : `PostgresPaymentAggregationStore`
- [x] `PaymentAggregatorTenantJob : ITenantJob` (Infrastructure/Aggregation)
- [x] `AggregateAllTrigger` (Contracts/Jobs) + `AggregateAllFanOutHandler` + DI

### D. Tests
- [x] Unit : calculator (multi-jour/taux, partiel, remboursement, non rattaché, Mixte,
      LivraisonBiens, FeeImputationMethod null, params null, vatOnDebits true, capacité PA absente),
      snapshot round-trip decimal
- [x] Integration : snapshot survit purge staging → agrégation ; multi-taux ; capacité absente ; TVA débits.
      (isolation tenant : structurelle — tables sans colonne tenant, connexion = tenant ; E2E 2 tenants existant)

### E. Docs
- [x] Pipeline INVARIANTS.md (INV-VENTILATION-*, agrégation), SCENARIOS.md, MODULE.md
- [x] TenantSettings INVARIANTS.md (FeeImputationMethod)

### F. Vérification
- [x] verify-fast (plateforme .NET 10 + agent net48 x86/x64) — PASS
- [x] run-tests (unit + integration) — PASS, 3845 tests ; intégration PIP03a 4/4 (Testcontainers)
- [ ] codex-review propre (ou P2 acceptés justifiés)

## Review (résultats)

Toutes les parties A–E livrées. `verify-fast` vert (plateforme + agent), `run-tests` vert (3845 tests).
Tests neufs : calculateur d'agrégation (19), garde d'échelle ventilation (3), parsing/entité FeeImputationMethod
(unit + intégration), intégration agrégation bout en bout (snapshot survit purge, multi-taux, capacité absente,
TVA sur débits) — tous exécutés et verts. Aucune règle fiscale inventée : Mixte/params null/capacité absente
SUSPENDUS avec motif opérateur ; fenêtrage de période + envoi réel + découpage Mixte différés à PIP03b (gelé).
