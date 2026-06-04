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

### VatexRequiredRule (VAL04 — F04 §3.4)
- Ligne exonérée (E, taux 0) sans VATEX → BLOQUANT `VATEX_MISSING` (n° de document cité, FieldRef BT-121) — INV-VALIDATION-008
- Ligne exonérée (E, taux 0) avec VATEX présent → aucune anomalie
- Ligne exonérée avec VATEX en blanc → BLOQUANT
- Ligne exonérée à taux ABSENT (null = 0) → VATEX toujours exigé → BLOQUANT
- Ligne standard (S) sans VATEX → aucune anomalie
- Plusieurs lignes : seule la ligne fautive est signalée
- Document sans ligne → aucune anomalie

### CategoryRateConsistencyRule (VAL04 — F03 §2.1, F04 §3.4 amendée)
- S / AA / AAA à taux 0 → BLOQUANT `CATEGORY_RATE_INCONSISTENT` — INV-VALIDATION-009
- S / AA / AAA à taux ABSENT (null) → BLOQUANT (taux positif requis)
- S 20 % / AA 10 % / AAA 2,1 % → aucune anomalie
- E / Z / AE / G / K / O à taux 20 % → BLOQUANT — INV-VALIDATION-009
- E / Z / AE / G / K / O à taux 0 (ou absent) → aucune anomalie
- Catégorie non résolue (régime non mappé) → ignorée ici (→ MappingCoverageRule)
- Document de marge 2 lignes (adjudication E 0 % + frais S 20 %) → aucune anomalie

### MappingCoverageRule (VAL04 — F04 §3.4)
- Ligne avec régime source + catégorie résolue → aucune anomalie
- Ligne avec régime source mais sans ventilation TVA → BLOQUANT `MAPPING_COVERAGE_MISSING` (code régime + n° doc cités, FieldRef BT-151) — INV-VALIDATION-010
- Ligne avec régime source + catégorie nulle → BLOQUANT
- Ligne SANS régime source (catégorie nulle) → hors périmètre, aucune anomalie
- Ligne multi-régimes, une part résolue + une non résolue → BLOQUANT
- Document entièrement non mappé → chaque ligne porteuse de régime bloque (filet de sécurité) — INV-VALIDATION-010

### CreditNoteRule (VAL04 — F04 §3.5, F07-F08)
- Document sans référence d'origine → non traité comme avoir, aucune anomalie, lookup jamais appelé
- Avoir valide (original connu & émis, montants positifs) → aucune anomalie — INV-VALIDATION-011
- Avoir dont l'original est inconnu → BLOQUANT `CREDIT_NOTE_ORPHAN` (n° avoir + n° original cités) — INV-VALIDATION-011
- Avoir dont l'original est connu mais non émis → BLOQUANT `CREDIT_NOTE_ORIGINAL_NOT_ISSUED`
- Avoir à montant de ligne négatif → BLOQUANT `CREDIT_NOTE_NEGATIVE_AMOUNT` — INV-VALIDATION-011
- Avoir à total négatif seul → BLOQUANT `CREDIT_NOTE_NEGATIVE_AMOUNT`
- Avoir à prix unitaire HT (BT-146) négatif → BLOQUANT `CREDIT_NOTE_NEGATIVE_AMOUNT`
- Avoir à charge/remise de document négative → BLOQUANT `CREDIT_NOTE_NEGATIVE_AMOUNT`
- Avoir à encaissement négatif → BLOQUANT `CREDIT_NOTE_NEGATIVE_AMOUNT`
- Avoir à référence d'origine sans numéro → BLOQUANT `CREDIT_NOTE_REF_MISSING`
- Avoir groupé (multi-références) : seule la référence non résolue est signalée
- Lookup `null` au constructeur → `ArgumentNullException`
- Annulation propagée (`OperationCanceledException`)

## Integration Tests

Aucun pour VAL01/VAL04 : framework et règles en mémoire (aucune base). VAL04 interroge le module Documents
derrière l'abstraction `IIssuedInvoiceLookup`, doublée par un fake en test unitaire ; les tests
d'intégration réels apparaîtront avec son implémentation (module Documents, lot TRK / TRK03) et la
persistance des anomalies avec le document.
