# Plan — Lisibilité du régime de la marge + aide à la déclaration de TVA

Décision Karl (2026-06-27, interactif) : **Option A — registre de marge persisté**.
Contexte : sous le régime de la marge, les factures portent TVA 0 (art. 297 E) ; le CP doit
déclarer lui-même la TVA sur sa marge (= commission totale, F03 §2.4/§270) dans sa CA3. Deux
besoins : (1) que la ligne marge ne soit pas confondue avec une exonération classique ;
(2) un récap mensuel de la base marge + TVA sur marge pour aider à remplir la CA3.

**Aucune règle fiscale inventée** : tous les chiffres viennent des cœurs purs déjà sourcés
(`B2cMarginResolver`, `B2cTransactionAggregationCalculator`, F03 §2.4/§2.5). Affichage = présentation pure.

---

## Livrable 1 — Lisibilité de la ligne marge (petit, faible risque) — FAIRE EN PREMIER

Sur le détail document, une ligne au régime de la marge (catégorie `E` + VATEX-EU-F/I/J) doit
afficher une mention explicite au lieu du sec « E — Exonéré (motif VATEX requis) ».

- Déclencheur = la **signature marge** (E + VATEX-EU-F/I/J), toujours correcte — PAS un « flag vertical »
  (il n'en existe pas dans la plateforme ; la mention est juste dès qu'une ligne EST en marge).
- Mention dérivée du VATEX (libellés déjà sourcés dans `VatexCatalog`) :
  - VATEX-EU-F → « Régime particulier – biens d'occasion »
  - VATEX-EU-I → « Régime particulier – œuvres d'art »
  - VATEX-EU-J → « Régime particulier – objets de collection et d'antiquité »
- Note sous le tableau quand au moins une ligne est en marge : *« Régime de la marge (art. 297 E) :
  TVA non récupérable par l'acheteur ; la TVA sur la marge est due par l'opérateur dans sa déclaration de TVA. »*

### Fichiers
- [ ] **Helper présentation pur** `src/Host/Liakont.Host/Components/MarginRegimeDisplay.cs`
  (modèle `VatCategoryDisplay.cs`). Entrée : `VatCategory? category, string? vatexCode`.
  Sortie : mention FR explicite ou `null` (pas marge). Codes marge = même HashSet ordinal que
  `B2cMarginMarking.MarginVatexCodes` (F03 §2.2). Aucun texte fiscal inventé.
- [ ] **Projection** : exposer le `VatCategory?` brut + `VatexCode` brut par ligne (ou un champ
  `MarginMention`) dans `DocumentContentView`/`DocumentLineProjection.FromPivot`
  (`src/Host/Liakont.Host/Documents/`). Vérifier ce qui est déjà exposé avant d'ajouter.
- [ ] **Razor** `DocumentDetailView.razor` (Détail des lignes, ~l.102-103) : si mention marge,
  l'afficher dans la cellule Catégorie (ou en sus du VATEX) ; ajouter la note de bas de tableau
  conditionnée à la présence d'au moins une ligne marge.
- [ ] **Tests bUnit** (règle 19) : ligne marge → mention + note présentes ; ligne taxable → inchangée ;
  doc sans marge → pas de note. (`tests/Host/Liakont.Host.Tests.Unit/...DocumentDetailView...`)
- [ ] verify-fast (Release), codex-review (-Engine claude), commit + push.

---

## Livrable 2 — Page « TVA / Déclaration » (registre de marge persisté)

### 2.1 Cœur : prédicat buyer-indépendant + résolveur partagé
- [ ] **`B2cMarginMarking.IsMarginRegime(enrichedPivot)`** PUBLIC buyer-INDÉPENDANT
  (`HasAuctionFees && Totals.TotalTax==0 && AllLinesUnderMarginRegime`). Expose la logique de régime
  SANS le critère B2C (≠ `IsMarginDeclaration` qui exige non-pro). Couvre B2C **et** B2B. (F03 §2.10,
  régime=contenu / canal=acheteur.)
- [ ] **Extraire** `ResolveMarginAsync` + `HasSeparateVat` de `B2cMarginAggregatorTenantJob.cs:227-276`
  dans un résolveur partagé `src/Modules/Pipeline/Application/B2cReporting/B2cMarginDocumentResolver.cs`
  (orchestration : pivot + `ITvaMappingService` + companyId → `B2cMarginResolution`). Le job appelle
  désormais ce résolveur (pas de duplication = pas de divergence P1).
- [ ] Conversion TTC→HT par taux : réutiliser `B2cTransactionAggregationCalculator` (decimal half-up,
  `HT=round(TTC/(1+taux))`, `TVA=TTC−HT`). Un doc marge = UN taux (fail-closed si mixte, F03 §2.3 pt 2).

### 2.2 Persistance (projection recalculable, PAS append-only)
- [ ] **Migration** `src/Modules/Pipeline/Infrastructure/Migrations/V008__create_margin_registry_table.sql`
  (modèle `V004__create_payment_aggregations_table.sql` = projection upsert, **sans** triggers WORM).
  Table `pipeline.margin_registry` : `document_id uuid PK`, `issue_date date NOT NULL`,
  `currency text NOT NULL`, `vat_rate numeric(6,4) NOT NULL`, `margin_base_ht numeric(18,2) NOT NULL`,
  `margin_vat numeric(18,2) NOT NULL`, horodatage. Clé d'upsert = `document_id` (un doc = un taux).
  Base TENANT (pas de colonne tenant — la connexion EST la frontière).
- [ ] **Entité** `Domain/B2cReporting/MarginRegistryEntry.cs` (modèle `B2cMarginEmissionEntry.cs`).
- [ ] **Store write** `Application/IMarginRegistryStore.cs` + `Infrastructure/Persistence/PostgresMarginRegistryStore.cs`
  (modèle `PostgresVentilationSnapshotStore.cs`, mais `ON CONFLICT (document_id) DO UPDATE` ; +
  `DeleteAsync(documentId)` pour le cas « n'est plus marge au re-CHECK »).
- [ ] **DTO + query read** `Contracts/MarginRegistryMonthlyDto.cs`, `Contracts/Queries/IMarginRegistryQueries.cs`,
  `Infrastructure/Queries/PostgresMarginRegistryQueries.cs` : `GROUP BY mois×taux`, `SUM(base_ht)`,
  `SUM(margin_vat)`, filtre `MonthPeriod.TryParse` (modèle `PostgresB2cMarginEmissionQueries.cs:40`).
- [ ] **DI** : 2 `AddScoped` dans `PipelineModuleRegistration.cs` (l.73/78).

### 2.3 Câblage au CHECK (calcul lecture seule dans l'evaluator, écriture côté appelant)
- [ ] `CheckDecision.cs` : porter la `MarginRegistryEntry?` résolue (comme `Ventilation`/`OperationCategory`).
- [ ] `DocumentCheckEvaluator.cs` (avant `Ready(...)`, ~l.235) : si `IsMarginRegime` + résolution OK,
  calculer l'entrée (lecture seule, MapAsync Frais — pas d'écriture, contrat « detection only » préservé).
- [ ] **Écriture** côté appelant, à côté de `WriteVentilationSnapshotAsync` :
  - `DocumentReceivedConsumer.cs:167` (CHECK initial) → upsert si marge / delete sinon.
  - `DocumentRecheckService.cs:166` (re-CHECK = recalcul) → idem (upsert/delete).

### 2.4 Page console (read mensuel)
- [ ] Service console Host `src/Host/Liakont.Host/TvaDeclaration/` (interface + impl via `ISender`/MediatR
  OU directement `IMarginRegistryQueries` — calquer `B2cMarginEmissionsConsoleQueryService`) + ViewModel.
- [ ] Page `Components/Pages/TvaDeclaration.razor` (modèle `B2cMarginEmissions.razor` : `@page`,
  `[Authorize(Policy = LiakontPermissions.Read)]`, sélecteur `<input type="month">`, triptyque
  erreur/chargement/contenu, tableau récap par taux + total). Aucune logique métier dans la page.
- [ ] DI `AppBootstrap.cs` (~l.718/751) + entrée menu `LiakontNavNodeProvider.cs` (sous Paramétrage ou
  consultation `liakont.read` — policy du nœud = policy de la page) + maj `LiakontNavNodeProviderTests`.
- [ ] **Tests bUnit** page (modèle `B2cMarginEmissionsTests.cs` : Fake + Spy période) + test query/handler.

### 2.5 Vérification + backfill
- [ ] Backfill : re-CHECK des docs marge existants peuple le registre (les mois passés sans re-CHECK
  restent vides — assumé ; le registre se remplit au fil de l'eau + au re-CHECK).
- [ ] verify-fast (Release), run-tests (Testcontainers : migration V008 + écriture CHECK + query),
  codex-review (-Engine claude), commit + push.

---

## Invariants à respecter (P1)
- Montants `decimal`, arrondi half-up 2 décimales (`PivotRounding`).
- `margin_registry` est une **projection** (upsert/recalcul) — JAMAIS de trigger WORM (≠ `b2c_margin_emissions`).
- Tenant-scopé (connexion = frontière). Aucune requête cross-tenant.
- Aucune règle fiscale inventée : régime + marge dérivés des cœurs purs sourcés.
- Page Blazor testée bUnit (règle 19), zéro logique métier dans la page.
