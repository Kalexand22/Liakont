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

---

# Partie 2 — Maillon plateforme : marquage de la déclaration B2C-marge (bloquant E2E)

> Le job B4 filtre sur `IsB2cReportingDeclaration` (via `B2cMarginDeclaration.Matches`). **Aucun code prod
> ne pose ce flag** (vérifié : l'agent ne le porte jamais ; CHECK/SEND/PivotEmitterEnricher ne font que le
> passer). Sans ce maillon, B4 n'a aucune entrée → pas de marge observable. Branche `feat/ereporting-b2c`.

## P2.0 — Sourçage (FAIT)
- [x] Critère sourcé F03 : **régime de la marge** (mapping VALIDÉ → catégorie E + VATEX-EU-F/I/J, §2.2/§2.3 ;
  jamais déduit mécaniquement du code régime — §3, table validée régime-par-régime) **+ B2C** (acheteur non
  professionnel, §2.4 commettant non assujetti / acquéreur particulier) **+ frais** (commission, §2.4)
  **+ 297 E** (aucune TVA distincte, §2.3). Fail-closed : signal manquant/ambigu → non marqué.

## P2.1 — Architecture (read-time, pattern émetteur/TVA — JAMAIS persisté)
- Le marqueur suit le pattern d'enrichissement read-time (PivotEmitterEnricher / mapping TVA) : **dérivé**
  au moment où le pivot enrichi (catégorie+VATEX du mapping validé) est disponible, **jamais** stagé (le
  staging garde le pivot SOURCE, hashé F06). Un seul point de dérivation.
- [x] **`B2cMarginMarking.IsMarginDeclaration(PivotDocumentDto enrichedPivot)`** (Domain.B2cReporting, PUR) :
  frais présents + toutes les lignes mappées E + VATEX∈{EU-F,EU-I,EU-J} + `Totals.TotalTax==0` + acheteur
  non pro (`Customer==null` ou Siren/Siret/VatNumber vides && !IsCompanyHint). Champs Contracts uniquement (PAS de
  référence à `Validation.Domain.CompanyHintDetector` — frontière P1).
- [x] **`CheckTvaMapping.Evaluate`** : après construction du pivot enrichi, dériver+poser le marqueur (Rebuild
  avec `isB2cReportingDeclaration: true` si `IsMarginDeclaration`, guard `!déjà marqué` pour ne pas effacer le
  marqueur B2C taxable B2C-01). → **SEND récupère le pivot marqué gratuitement** (ReadStagedPivotAsync utilise
  `evaluation.EnrichedDocument`) → `Matches` ligne 517 aiguille. **Zéro changement SEND.** CHECK l'obtient aussi.
- [x] **`B2cMarginAggregatorTenantJob` (B4)** : dans la découverte, pré-filtre cheap (`HasMarginFees`) →
  enrichir via `CheckTvaMapping.BuildPlan/MapAsync/Evaluate` (`EnrichForMarginMarkingAsync`) → `Matches(enriched)`
  → `ResolveMarginAsync(enriched)`. (Avant : `Matches(rawPivot)` toujours faux.)

## P2.2 — Tests + vérif
- [x] Unit `B2cMarginMarking` (16 tests) : marge (E+VATEX-EU-J/F/I + frais + B2C + TVA=0) → true ; taxable (S,
  TVA>0) → false ; B2B (SIREN/Siret/VAT/companyHint) → false ; sans frais → false ; E sans VATEX marge
  (hors-champ) → false ; VATEX non-marge → false ; lignes mixtes → false ; acheteur anonyme → true (B2C).
- [x] Unit `CheckTvaMapping` (2 tests) : pivot marge enrichi porte le marqueur ; cas taxable ne le porte pas.
- [x] Intégration B4 (15 verts) : fixture RÉALISTE (pas de pré-marquage — l'agent ne marque jamais ; la plateforme
  dérive), table seedée (MARGE, Part.Autre)→E+VATEX-EU-J ; doc marge découvert/agrégé/transmis ; adjudication
  taxable → non marqué/ignoré ; fail-closed (frais non mappé) ; D1/D2/D3 préservés.
- [x] verify-fast (3 sols) PASS + build Release (StyleCop) PASS + run-tests 6810 PASS.

## P2.4 — codex-review round 1 : 0 P1, 3 P2 → CORRIGÉS
- [x] **P2 #1 (fail-closed)** : un doc à la FORME d'une marge (frais + TVA=0) non classé marge (exonéré non-marge,
  ou acheteur pro) filait par la voie document → honoraires perdus. **Fix** : `B2cMarginMarking.LooksLikeUnclassifiedMargin`
  + garde CHECK (`DocumentCheckEvaluator`) qui BLOQUE avec message opérateur FR (n°12), symétrique au pré-filtre B4.
  Un doc taxable (TVA>0) garde sa voie nominale (représentation commission→ligne taxable = item adaptateur Part 1, noté).
- [x] **P2 #2 (trou de test)** : ajout test intégration B4 « acheteur SIREN → non marqué, non agrégé » (anti-régression B2B).
- [x] **P2 #3 (dette)** : 2 `MapAsync`/doc candidat — laissé tel quel (build), commentaire de dette tracé dans le job.
- [x] Tests ajoutés : +6 unit `LooksLikeUnclassifiedMargin`, +1 intégration B4 (B2B), +1 intégration CHECK (garde fail-closed).

## P2.3 — Dépendance E2E (Partie 4, noter)
- ⚠️ La table validée du déploiement doit mapper le régime marge **en Part.Autre** (= `CheckTvaMapping.LinePart`)
  → E + VATEX-EU-J. Le seed `config/exemples/tenant-seed/encheres/mapping-tva.json` actuel utilise
  `NORMAL/MARGE` + `Adjudication/Frais` (désaligné avec l'adaptateur qui extrait 5/6 et le Check qui mappe en
  Autre). **À aligner en Partie 4** (orchestration démo), hors périmètre du maillon de marquage.

## Partie 4 — Orchestration démo « Enchères » observable (LEAN — décision Karl 2026-06-24)
Décisions Karl : **lean d'abord** (observabilité via pages existantes /traitements + /documents ; la page
dédiée des émissions marge = lot suivant) + **tout préparer, STOP avant l'envoi réel SuperPDP** (PA Fake
en mémoire ; swap SuperPDP documenté). Aucun code C# de prod touché (config/scripts/docs uniquement).
- [x] **Seed démo 2 tenants** `deployments/encheres-demo/tenant-seed/{volontaire,judiciaire}/` :
  `tenant-profile.json` (SIREN fictifs **vérifiés non attribués** data.gouv : 976543215 SVV / 960123453 SCP ;
  `operationCategory: "Mixte"` — requis sinon CHECK bloque tout, sourcé F09 §1), `pa-accounts.json` (Fake
  Staging, sans secret), `mapping-tva.json` (**codes RÉELS 5→S 20 % / 6→E+VATEX-EU-J en Part.Autre**, F03
  §2.1/§2.3, NON VALIDÉE → PIP01 actif ; autres régimes bloquent = fail-closed). Résout la dépendance P2.3.
- [x] **`demo.ps1`** (ASCII-only, convention du dossier) : `source` (CREATE DB + import idempotent + SIREN +
  **login lecture seule `liakont_encheres_ro` db_datareader**, n°5), `agent-config` (génère 2 agent.json
  dossier 2/1 + schema enc, secrets en placeholders à chiffrer DPAPI sur le poste agent), `status`, `help`.
  Testé de bout en bout sur la base montée (status/source/agent-config OK).
- [x] **README runbook** : parcours complet (plateforme bucodi reset → source → 2 tenants console → mapping +
  nature Mixte + PA Fake + SIREN + agent → run → **déclencher B4 via l'admin des planifications** (cadence
  déploiement, cron null) → observer) + section envoi RÉEL SuperPDP séparée (sur décision).
- [x] `.gitignore` : `.secrets.local.json` + `agent/` (anti-fuite secrets, n°10).
- [x] codex-review (engine claude) : round 1 = **1 P1** (faux-vert : `operationCategory: null` → CHECK bloque
  tout → B4 agrège 0) **corrigé** (Mixte dans les 2 profils + étape runbook) ; **round 2 = CLEAN**.
## Partie 5 — Page console des émissions marge B2C (FAIT 2026-06-24)
Objectif : rendre OBSERVABLE en console le journal `pipeline.b2c_margin_emissions` (état Pending→Émis, id
PA, détail). Journal **sans montants** (par conception) → la page trace le CYCLE d'émission, pas une règle
fiscale. Calquée sur `/encaissements` + gabarit **DeclaredListPage** (jamais de grille maison — P1
[[console-web-stratum-design-system]]). Décision « lean » : pas de bandeau capacité (éviterait d'élargir
`PaCapabilitiesSummaryDto` cross-module) — la page = filtre période + liste + états vide/erreur.
- [x] **Maille = une TRANSMISSION (un POST)** : regroupement par `emission_batch_id` (PAS `content_hash` —
  deux POST d'un même contenu le partagent ; les fusionner masquerait une transmission, P2 review attrapé).
  Ajout colonne **V007** `emission_batch_id` (un lot/POST, partagé Pending+issue) + champ entry + store + job.
- [x] **Query** `IB2cMarginEmissionQueries` (Contracts.Queries) + `PostgresB2cMarginEmissionQueries`
  (Infrastructure.Queries) — tenant-scopée (IConnectionFactory), CTE + `ROW_NUMBER` (dernière entrée) +
  `COUNT(DISTINCT document_id)` (PG n'admet pas COUNT DISTINCT en fenêtre). DTO `B2cMarginEmissionAggregateDto`.
- [x] **DI** PipelineModuleRegistration + AppBootstrap (service Host). **Nav** : entrée « Émissions marge B2C ».
- [x] **Host** : Row + ColumnRegistry + StatusDisplay (Émis/engagée/rejeté/échec) + ViewModel + service de composition.
- [x] **Page** `/emissions-marge-b2c` (DeclaredListPage, filtre période, `[Authorize(Read)]`) + CSS.
- [x] **Tests** : bUnit page (5, P1 règle 19) + service unit (3) + StatusDisplay (3) + intégration query
  (rollup par lot, + régression collision content_hash → 2 lignes jamais fusionnées).
- [x] verify-fast (3 sols) + run-tests (6828, 0 échec) + Release/StyleCop (0 warn) + **codex-review : r1 2 P2
  (maille content_hash collapse + trou test) → r2 1 P2 (StatusDisplay non testé) → r3 3 P2 (docs périmées) →
  r4 CLEAN**. À committer/pusher.

- Reste (hors lot, lean) : provisioning 100 % automatisé
  (bloqué par modèle console-driven : tenants/secrets/clé agent en console par conception) ; flux factures
  clients + notes hono (#7) ; lot PDF GED ; envoi réel SuperPDP (décision Karl).

---

# BUG-5 — Détail des lignes au READ-TIME (rejeu mapping), états Bloqué / Prêt-à-envoyer (Option B)

> Branche `fix/bug-5-lignes-readtime`. Le détail des lignes ne s'affichait qu'APRÈS transmission (projection
> depuis le `payload_snapshot` de `document_events`). On veut les voir AVANT — c'est là qu'on diagnostique un
> blocage. Approche read-time replay : relire le pivot SOURCE stagé + rejouer le MÊME moteur de mapping.

## Plan
- [x] **Pipeline.Contracts** : `IDocumentContentReplayService.ReplayAsync(documentId, ct)` → `DocumentContentReplay`
  (`{ PivotDocumentDto? MappedPivot; bool Available }`). Tenant-scopé (résolu par la requête). Retourne le pivot
  ENRICHI si le mapping passe, le pivot SOURCE si le mapping bloque (catégorie/VATEX vides = diagnostic factuel),
  ou `Available=false` si staging absent/intègre KO (purgé après émission) → fallback Host snapshot.
- [x] **Pipeline.Infrastructure** : `DocumentContentReplayService` (sibling de `DocumentRecheckService`) : lit
  `IPayloadStagingStore.ReadAsync(StagedPayloadKey(tenantId, documentId, payloadHash))` + `PivotCanonicalJsonReader`
  → `CheckTvaMapping.BuildPlan` + `ITvaMappingService.MapAsync(companyId, …)` + `CheckTvaMapping.Evaluate`. Ready →
  `EnrichedDocument` ; Blocked → pivot source. Aucune valeur inventée (P1 n°2). DI dans `PipelineModuleRegistration`.
- [x] **Host** : `DocumentLineProjection.FromPivot(PivotDocumentDto)` (corps partagé extrait de `FromTransmittedSnapshot`).
  `DocumentDetailConsoleQueryService` PRIORISE le snapshot transmis (vérité d'audit, doc émis/refusé) puis, EN
  L'ABSENCE de snapshot (non transmis), consomme le replay → `FromPivot` (correctif review P2 round 1).
- [x] **Razor** : placeholder ajusté (« dès que lu et contrôlé »), ne subsiste qu'en l'absence RÉELLE de lignes
  (`Content.HasLines`). Le tableau existant est réutilisé tel quel.
- [x] **Tests** : bUnit (doc Bloqué AVEC lignes + régime source, catégorie/VATEX vides ; préséance snapshot ;
  robustesse rejeu) ; intégration Pipeline (replay : Ready → catégorie S/taux posés, Blocked → régime source sans
  catégorie, staging absent → Available=false, doc inconnu → Unavailable).
- [x] verify-fast (3 sols) PASS + Release/StyleCop PASS + run-tests 6854 PASS + codex-review (r1 1 P2 préséance
  snapshot → r2 1 P2 OCE avalée → r3 CLEAN). Push `fix/bug-5-lignes-readtime`.
