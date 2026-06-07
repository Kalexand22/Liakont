# PIP04 — Rectificatifs e-reporting (flux RE annule-et-remplace)

Session orchestration : `orch-20260607-010607-slot1` · slot-1 · sous-branche `feat/pipeline-PIP04`.

## Source fiscale (jamais inventée)
- F07-F08 §B.1 : correction e-reporting (B2C / paiements) = **flux rectificatif type RE** qui
  **annule et remplace l'ensemble des données agrégées de la période** (par SIREN + période).
- F09 §5.4 : trop-perçu / remboursement = montant négatif dans l'agrégat (via rectificatif RE — cf. F7).
- Périmètre item (re-découpage 2026-06-06) : PIP04 = **mécanisme RE** (builder + idempotence + capacité +
  historique append-only) sur l'infra d'agrégation **PIP03a**. Les rectificatifs de PAIEMENT (10.4) ne
  portent de données réelles qu'une fois **PIP03b** actif (fenêtrage + envoi, GELÉ). Le mécanisme RE des
  e-reporting B2C (10.3) + correction sur avoir/altération source restent V1.

## Conception (100 % dans le module Pipeline — frontières respectées)
- Projection PIP03a `pipeline.payment_aggregations` (jour×taux, `IPaymentAggregationStore.GetAllAsync`)
  = source des lignes corrigées. Les bornes de période sont une ENTRÉE (pas de fenêtrage = PIP03b).
- `SendPaymentReportAsync(PaymentReportPeriod{Flux,Start,End})` existe déjà (ne porte pas de lignes — PIP03b
  les enrichira). Capacité `SupportsReportRectification` existe déjà.
- Journal `pipeline.report_rectifications` APPEND-ONLY (triggers base) — DISTINCT de `payment_aggregate_events`
  (audit de transmission écrit par PIP03b) et de la projection (recalculée).

## Tâches
- [ ] Domain : `RectificationLine`, `ReportRectification`, `ReportRectificationStatus`, `RectificationBuilder`
      (pur, decimal-only, empreinte SHA-256 déterministe — annule-et-remplace, toutes les lignes de la période).
- [ ] Application : `IReportRectificationLedger` + `ReportRectificationEntry` (+ réf Transmission.Contracts pour `PaymentReportFlux`).
- [ ] Contracts : `PipelineRunType.Rectify` (+ `RectifyReportsAllTrigger`).
- [ ] Infrastructure :
      - `V005__create_report_rectifications_table.sql` (append-only, triggers UPDATE/DELETE/TRUNCATE).
      - `PostgresReportRectificationLedger` (Dapper, montants en chaînes invariantes, jamais float).
      - `ReportRectificationService` (build -> idempotence -> capacité -> transmission Fake -> journal + RunLog).
      - `ReportRectificationTenantJob` (ITenantJob) + `RectifyReportsAllFanOutHandler` (fan-out SOL06).
      - DI dans `PipelineModuleRegistration`.
- [ ] Tests.Unit : `RectificationBuilderTests` (complétude, déterminisme du hash, decimal, filtrage bornes, tri).
- [ ] Tests.Integration : `ReportRectificationIntegrationTests` (Testcontainer Postgres) —
      avoir sur période déclarée, rectificatif manuel, PA sans capacité (PendingCapability, aucun envoi),
      idempotence (double déclenchement = 1 seule transmission), append-only (UPDATE/DELETE rejeté).
- [ ] Docs module : INV-PIPELINE-033..036 + SCENARIOS.
- [ ] verify-fast vert · run-tests vert · codex-review propre.

## Invariants clés (anti-régression fiscale)
- Montants `decimal`, jamais float (hash + persistance via chaînes invariantes).
- Aucune règle fiscale inventée : le rebuild ne fait que re-sommer les lignes existantes de la période.
- Capacité absente = en attente (jamais d'envoi à l'aveugle, jamais de blocage produit).
- Journal append-only (triggers base) ; ancien état jamais effacé.
- Pipeline ne référence aucun plug-in PA concret (NetArchTest) ; tenant-scopé.
