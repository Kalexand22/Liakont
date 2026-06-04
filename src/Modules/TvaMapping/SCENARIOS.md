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
- `IsValidated_Is_False_When_Validation_Absent` / `..._True_When_Both_Set` /
  `..._False_When_Only_ValidatedBy_Set` — état « NON VALIDÉE » (INV-006).
- `Reconstitute_Invalid_Table_Throws_At_Load` — re-validation au chargement (INV-005).

### VatCategoryParserTests (INV-TVAMAPPING-001)
- `Parse_Admitted_Codes_Succeeds` — les 9 codes UNCL5305 admis.
- `Parse_Trims_Whitespace` — espaces de bord tolérés.
- `Parse_Unknown_Or_Empty_Throws` / `Parse_Null_Throws` / `Parse_Numeric_Value_Is_Rejected` —
  toute valeur hors liste (dont une valeur numérique) rejetée, jamais devinée.

## Integration (`Tests.Integration`, Testcontainers PostgreSQL)

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
