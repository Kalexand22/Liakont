# Validation Module — Test Scenarios

## Unit Tests (Tests.Unit/)

### ValidationPipelineTests
- Document conforme (toutes les règles ne retournent rien) → résultat valide, aucune anomalie — INV-VALIDATION-003
- Agrégation : les anomalies de toutes les règles sont collectées (alertes + blocages) — INV-VALIDATION-002
- Document avec uniquement des alertes → reste valide (Warning n'invalide pas) — INV-VALIDATION-003
- Une règle qui crash → anomalie BLOQUANTE `RULE_CRASHED` (message citant le n° de document,
  détail technique journalisant l'exception) ET les autres règles s'exécutent quand même — INV-VALIDATION-001, INV-VALIDATION-002
- Une règle qui retourne `null` est tolérée (pas de NullReferenceException) — INV-VALIDATION-002
- L'annulation est propagée (`OperationCanceledException`), jamais convertie en `RULE_CRASHED` — INV-VALIDATION-005
- Un ensemble de règles vide est accepté à la construction
- Un contexte `null` est rejeté (`ArgumentNullException`)

### DocumentValidationContextTests
- Document `null` rejeté (`ArgumentNullException`) — INV-VALIDATION-006
- `companyId` vide (`Guid.Empty`) rejeté (`ArgumentException`) — INV-VALIDATION-006
- Arguments valides acceptés (document et companyId non vide accessibles) — INV-VALIDATION-006

### ValidationResultTests
- Résultat avec uniquement des alertes → valide — INV-VALIDATION-003
- Résultat avec au moins une anomalie bloquante → invalide — INV-VALIDATION-003
- Résultat vide → valide
- Une anomalie exige un code et un message non vides — INV-VALIDATION-004
- Fabrique `Blocking` : pose la sévérité bloquante et les champs (code, détail, FieldRef)
- Fabrique `Warning` : pose la sévérité alerte ; détail et FieldRef optionnels à `null`

### LineTotalsRuleTests (VAL03)
- Document cohérent (Σ lignes HT/TVA = totaux) → aucune anomalie — INV-VALIDATION-008
- Σ lignes HT ≠ total HT → BLOQUANT `DOC_TOTAL_MISMATCH` — INV-VALIDATION-008
- Σ lignes TVA ≠ total TVA → BLOQUANT `DOC_VAT_TOTAL_MISMATCH` — INV-VALIDATION-008
- Écart d'arrondi d'un centime → BLOQUANT (aucune tolérance, EN 16931) — INV-VALIDATION-008
- Arrondi half-up appliqué à la somme des lignes (1160,005 → 1160,01) → cohérent — INV-VALIDATION-008
- Document sans ligne → ignoré par cette règle (blocage porté par StructureRule) — INV-VALIDATION-008
- Charge document incluse dans la réconciliation HT (BR-CO-13) → cohérent — INV-VALIDATION-008
- Remise document incluse dans la réconciliation HT (BR-CO-13) → cohérent — INV-VALIDATION-008
- Écart HT avec charge document → BLOQUANT (réconciliation stricte) — INV-VALIDATION-008
- Réconciliation TVA court-circuitée si charge/remise document présente (TVA non résolue avant TVA04) → pas de faux positif — INV-VALIDATION-008

### ArithmeticRuleTests (VAL03)
- TTC = HT + TVA → aucune anomalie — INV-VALIDATION-008
- TTC ≠ HT + TVA (BR-CO-15) → BLOQUANT `DOC_ARITHMETIC_MISMATCH` — INV-VALIDATION-008
- Écart d'arrondi d'un centime → BLOQUANT (tolérance 0) — INV-VALIDATION-008

### SourceTotalsRuleTests (VAL03)
- Pas de total source (`null`) → aucune anomalie — INV-VALIDATION-009
- Total source = total passerelle → aucune anomalie — INV-VALIDATION-009
- Total source ≠ total passerelle → ALERTE `DOC_TOTAL_SOURCE_MISMATCH` (Warning, n'invalide pas) — INV-VALIDATION-009

### StructureRuleTests (VAL03)
- Document cohérent (1 ligne, date 2024, EUR) → aucune anomalie — INV-VALIDATION-010
- Aucune ligne → BLOQUANT `DOC_NO_LINES` — INV-VALIDATION-010
- Date dans le futur → BLOQUANT `DOC_DATE_FUTURE` (horloge injectée) — INV-VALIDATION-010, INV-VALIDATION-011
- Date du jour → pas considérée comme future — INV-VALIDATION-011
- Date antérieure à 2000 → ALERTE `DOC_DATE_TOO_OLD` — INV-VALIDATION-010
- Devise hors ISO 4217 → BLOQUANT `DOC_CURRENCY_INVALID` — INV-VALIDATION-010
- Devise vide → BLOQUANT `DOC_CURRENCY_INVALID` — INV-VALIDATION-010
- Devise en minuscules (« eur ») → acceptée (insensible à la casse) — INV-VALIDATION-010

### UniquenessRuleTests (VAL03)
- Numéro unique (non émis) → aucune anomalie — INV-VALIDATION-012
- Numéro déjà émis → BLOQUANT `DOC_NUMBER_DUPLICATE` — INV-VALIDATION-012
- Numéro absent → BLOQUANT `DOC_NUMBER_MISSING` ET le module Documents n'est pas interrogé — INV-VALIDATION-012
- La recherche est tenant-scopée (CompanyId du contexte) via le port `IIssuedDocumentLookup` — INV-VALIDATION-013

### Iso4217CurrenciesTests (VAL03)
- Codes ISO 4217 valides (EUR, USD, GBP, XOF, casse indifférente) → acceptés — INV-VALIDATION-010
- Codes inconnus, mal formés ou vides (ZZZ, XYZ, EU, EURO, vide, espaces, null) → rejetés — INV-VALIDATION-010

## Integration Tests

Aucun pour VAL01-VAL03 : le framework et les règles de cohérence sont en mémoire. VAL03 interroge le
module Documents via le port `IIssuedDocumentLookup` mais avec un **faux d'essai** (acceptance VAL03) ;
aucune base ni dépendance externe. Les tests d'intégration apparaîtront avec l'implémentation réelle du
port (module Documents, lot TRK — TRK03) et la persistance des anomalies avec le document.
