# Scénarios de test — module TvaMapping

## Unit (`Tests.Unit`)

### MappingTableTests (validation structurelle — INV-TVAMAPPING-001..006)
- `Create_Valid_Table_Succeeds` — table valide (S/AA/E+VATEX) acceptée.
- `Create_Empty_Table_Is_Valid` — une table sans règle est valide (bloque tout régime).
- `Create_E_AtZero_Without_Vatex_Throws` — E à 0 % sans VATEX rejeté (INV-002).
- `Create_E_AtZero_With_Vatex_Succeeds` — E à 0 % avec VATEX accepté.
- `Create_Duplicate_Code_And_Part_Throws` — doublon (code, part) rejeté (INV-003).
- `Create_Same_Code_Different_Part_Is_Allowed` — régime de la marge (adjudication + frais) accepté.
- `Create_Fixed_Rate_Without_Value_Throws` / `Create_Computed_Rate_With_Fixed_Value_Throws` /
  `Create_Computed_Rate_Without_Value_Succeeds` / `Create_Negative_Rate_Throws` — cohérence du taux (INV-004).
- `Create_Unknown_Category_Throws` — catégorie hors UNCL5305 rejetée (INV-001).
- `Create_Empty_Version_Throws` — version obligatoire.
- `Create_Aggregates_All_Violations` — toutes les violations remontées d'un coup.
- `Create_E_Computed_Without_Vatex_Throws` — E exige un VATEX même en taux calculé (INV-002).
- `Create_E_With_Fixed_NonZero_Rate_Throws` — E (exonéré) avec taux fixe non nul rejeté (INV-002/004).
- `Create_Same_Code_And_Part_Different_Flags_Still_Throws` — l'unicité (code, part) ignore les flags (INV-003).
- `Create_Wildcard_Code_Throws` — le joker `*` pré-v6 est refusé (INV-011, amendement F03 §4.1 du 2026-06-04).
- `IsValidated_Is_False_When_Validation_Absent` / `..._True_When_Both_Set` /
  `..._False_When_Only_ValidatedBy_Set` — état « NON VALIDÉE » (INV-006).
- `Reconstitute_Invalid_Table_Throws_At_Load` — re-validation au chargement (INV-005).

### VatCategoryParserTests (INV-TVAMAPPING-001)
- `Parse_Admitted_Codes_Succeeds` — les 9 codes UNCL5305 admis.
- `Parse_Trims_Whitespace` — espaces de bord tolérés.
- `Parse_Unknown_Or_Empty_Throws` / `Parse_Null_Throws` / `Parse_Numeric_Value_Is_Rejected` —
  toute valeur hors liste (dont une valeur numérique) rejetée, jamais devinée.

### TvaMapperTests (moteur TVA02 — INV-TVAMAPPING-007/008/010)
- `Map_FixedStandardRate_ProducesCategoryRateAndTrace` — régime assujetti → S/20 % + trace complète (rang, libellé, version).
- `Map_MargeExoneration_ProducesE_WithVatex` — adjudication marge → E/0 %/VATEX-EU-J (F03 §2.3).
- `Map_ReducedRate_ProducesAA` — taux réduit AA/10 %.
- `Map_UnknownRegime_IsBlocked` — régime non couvert → block (motif opérateur FR + action), jamais deviné (INV-007).
- `Map_KnownCodeButWrongPart_IsBlocked` — code connu mais part non couverte → block.
- `Map_SameCodeDifferentPart_SelectsCorrectRule` — marge : même code → adjudication (E) + frais (S), sélection par part.
- `Map_RuleWithFlags_AllSatisfied_IsMapped` — flags requis satisfaits (extras ignorés) → règle appliquée (F03 §3).
- `Map_RuleWithFlags_NotSatisfied_IsBlocked` / `..._DocumentHasNoFlags_IsBlocked` — flags non satisfaits → block (jamais une 2ᵉ règle, INV-003).
- `Map_RuleWithoutFlags_DocumentExtraFlagsIgnored_IsMapped` — règle sans flag = inconditionnelle.
- `Map_ComputedRateRule_LeavesRateNullWithComputedMode` — taux calculé : mode signalé, valeur résolue en aval (F03 §4.1).
- `Map_ValidatedTable_TraceCarriesValidationIdentity` / `Map_NonValidatedTable_StillMaps_TraceFlaggedNonValidee` — état de validation porté par la trace (INV-006).
- `Map_TenantIsolation_UsesOnlyProvidedTable` — moteur sans état : ne consulte que la table fournie (INV-008/010).
- `Map_FraisOfUnmappedRegime_IsBlocked` — pas de joker : la part frais d'un régime sans règle explicite est bloquée (INV-011).
- `Map_NullTable_Throws` / `Map_NullRequest_Throws` / `Map_EmptyTable_BlocksEveryRegime` — garde-fous d'entrée.

### MappingCoverageAnalyzerTests (détection de couverture TVA03 — INV-TVAMAPPING-012)
- `Analyze_AllObservedRegimesMapped_VerdictComplete` — tous les régimes observés couverts → complet.
- `Analyze_SomeObservedRegimesUnmapped_VerdictIncomplete_ListsAbsent` — régime absent listé avec code/libellé/occurrences/horodatage.
- `Analyze_NoObservedRegimes_VerdictComplete_EmptyLists` — tenant sans régime remonté → complet, listes vides.
- `Analyze_NoTableConfigured_AllObservedAbsent_VerdictIncomplete` — aucune table → tous absents, `IsTableConfigured=false`.
- `Analyze_NoTable_NoObservedRegimes_VerdictComplete` — ni table ni régime → complet, rien à faire.
- `Analyze_CodeMatchingIsOrdinalCaseSensitive` — comparaison EXACTE : casse différente → absent (cohérence moteur INV-011).
- `Analyze_CodeCoveredAcrossMultipleParts_CountedOnceAsCovered` — code présent dans plusieurs règles (adjudication + frais) → couvert une fois.
- `Analyze_ReflectsTableVersionAndValidationState` — version + état « NON VALIDÉE » portés par le rapport (INV-006).
- `Analyze_NullObservedRegimes_Throws` — garde-fou d'entrée.

### GetMappingCoverageReportHandlerTests (câblage TVA03 — INV-TVAMAPPING-008/012)
- `Handle_RoutesResolvedSlugToRegimeQuery_AndCompanyIdToMappingQuery` — **isolation tenant** : slug (`ITenantContext`) vers les régimes observés, `company_id` (`ICompanyFilter`) vers la table ; jamais de clé croisée.
- `Handle_AllRegimesMapped_ReturnsCompleteDto` / `Handle_SomeRegimesUnmapped_ReturnsIncompleteDto_WithAbsentList` — verdict + listes mappés en DTO.
- `Handle_NoTableConfigured_ReturnsIncompleteNotConfigured` — aucune table → incomplet, `IsTableConfigured=false`.
- `Handle_NoObservedRegimes_ReturnsComplete` — tenant sans régime remonté → complet.
- `Handle_UnresolvedTenant_Throws` (slug `null`/vide/blanc) — aucune lecture tant que le tenant n'est pas résolu (message opérateur FR + action).

> **Tests d'intégration (Testcontainers) : non requis pour TVA03 (node `integration_tests` sauté).**
> La détection est du croisement PUR (analyseur de domaine) au-dessus de DEUX lectures déjà couvertes
> par des tests d'intégration Postgres en amont : la table de mapping par `company_id`
> (`MappingTablePersistenceIntegrationTests`, TVA01, dont `Tenant_Isolation_Is_Enforced`) et les
> régimes source observés par slug (`SourceTaxRegime*` du module Ingestion, PIV04, isolation tenant
> incluse). Le risque propre à TVA03 — passer la BONNE clé de tenant à chaque lecture — est vérifié
> EN DIRECT par `Handle_RoutesResolvedSlugToRegimeQuery_AndCompanyIdToMappingQuery`. Aucun nouveau
> schéma ni nouvelle requête SQL n'est introduit. (Même justification que TVA02 « moteur pur ».)

## Integration (`Tests.Integration`, Testcontainers PostgreSQL)

### Endpoints console API04 (`tests/Liakont.Console.Api.Tests.Integration/TvaMappingEndpointsIntegrationTests`)
> Tests d'intégration in-process (harness HTTP API01a + Testcontainers). Les MUTATIONS portent sur un tenant dédié (`tenant-api04`).
- `GetTvaMapping_Without_Authentication_Returns_401` / `..._Without_Read_Permission_Returns_403` — accès en lecture protégé par `liakont.read`.
- `GetTvaMapping_As_Reader_Returns_Configured_Table` — la table du tenant (+ règles) est exposée.
- `GetTvaMapping_Is_Company_Scoped` / `..._Tenant_Isolation_Empty_Table_In_Other_Tenant` — scoping par société et par tenant (CLAUDE.md n°9, table absente = `null`).
- `Edit_Lifecycle_As_Settings_User_Goes_Through_Tva05_Engine` — ajout (→ invalidation + journal `AddRule`), modification, suppression, re-validation : toute mutation passe par le moteur TVA05.
- `AddRule_As_Actions_User_Without_Settings_Returns_403` / `AddRule_Without_Authentication_Returns_401` / `ValidateMapping_As_Reader_Without_Settings_Returns_403` — l'édition exige `liakont.settings`.
- `UpdateRule_Route_Body_Mismatch_Returns_400` — la clé (code régime / part) de l'URL doit correspondre au corps.

### MappingTablePersistenceIntegrationTests (INV-TVAMAPPING-004..008)
- `Insert_And_Get_RoundTrips_All_Fields` — en-tête + règles persistés et relus à l'identique (taux decimal exact).
- `Get_Returns_Null_When_No_Table_For_Tenant` — absence de table = `null`.
- `Rule_With_SourceFlags_RoundTrips` — flags source (jsonb) préservés (INV-009).
- `Computed_Rate_Rule_RoundTrips_With_Null_Rate` — taux calculé persiste avec valeur nulle (INV-004).
- `NonValidated_Table_Is_Loadable_And_Flagged_NonValidee` — table « NON VALIDÉE » chargeable, état exposé (INV-006).
- `Tenant_Isolation_Is_Enforced` — deux tenants, lectures scopées (INV-008).
- `Insert_Duplicate_Table_For_Same_Tenant_Throws_Conflict` — unicité par tenant (INV-008).
- `Load_Of_Structurally_Invalid_Table_Throws_At_Load` — E à 0 % sans VATEX inséré en SQL brut → exception au chargement (INV-005).
- `Load_Of_Unknown_Category_Throws_At_Load` — catégorie hors liste insérée en SQL brut → exception au chargement (INV-001/005).
