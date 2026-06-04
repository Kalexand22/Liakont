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

### Validateurs d'identité (VAL02 — Domain/Identity)

#### SirenValidatorTests
- SIREN à clé de Luhn valide (123456782, 000000000) → valide — INV-VALIDATION-008
- SIREN à clé de Luhn invalide (123456789, 111111111) → invalide — INV-VALIDATION-008
- SIREN de La Poste (356000000) → valide (dérogation F04 §4.1) — INV-VALIDATION-008
- Mauvaise forme (null, vide, longueur ≠ 9, non numérique) → invalide — INV-VALIDATION-008

#### SiretValidatorTests
- SIRET à clé de Luhn valide (12345678200002, 00000000000000) → valide — INV-VALIDATION-008
- SIRET à clé de Luhn invalide → invalide — INV-VALIDATION-008
- Mauvaise forme (null, vide, longueur ≠ 14, non numérique) → invalide — INV-VALIDATION-008

#### FrenchVatNumberValidatorTests
- Clé cohérente avec le SIREN (FR11123456782, FR12000000000), préfixe insensible à la casse (fr…) → valide — INV-VALIDATION-009
- Clé incohérente → invalide — INV-VALIDATION-009
- Mauvaise forme (préfixe ≠ FR, longueur ≠ 13, clé/SIREN non numériques) → invalide — INV-VALIDATION-009

#### CountryCodeValidatorTests
- Code alpha-2 officiel (FR, DE, US, GB, AQ, casse indifférente) → valide — INV-VALIDATION-010
- Code non assigné / user-assigned (XK, ZZ, QZ) → invalide — INV-VALIDATION-010
- Mauvaise forme (null, vide, longueur ≠ 2, non alphabétique) → invalide — INV-VALIDATION-010

### Règles d'identité (VAL02 — Domain/Rules)

#### SupplierIdentityRuleTests
- Profil tenant configuré + SIREN document cohérent → aucune anomalie — INV-VALIDATION-011
- Document sans SIREN émetteur → accepté (la référence vient du profil) — INV-VALIDATION-011
- Profil tenant absent → `SUPPLIER_SIREN_MISSING` bloquant (cite le n° de document) — INV-VALIDATION-011
- SIREN émetteur du profil invalide (Luhn) → `SUPPLIER_SIREN_INVALID` bloquant — INV-VALIDATION-011
- SIREN document ≠ SIREN profil → `SUPPLIER_SIREN_MISMATCH` bloquant (cite les deux SIREN) — INV-VALIDATION-011
- SIRET émetteur du document invalide (Luhn) → `SUPPLIER_SIRET_INVALID` bloquant ; SIRET valide → accepté — INV-VALIDATION-011
- La lecture du profil est scopée au `CompanyId` du contexte — INV-VALIDATION-011, INV-VALIDATION-006
- Constructeur : `ITenantSettingsQueries` nul rejeté (`ArgumentNullException`)

#### BuyerIdentityRuleTests
- Pas d'acheteur identifié (Customer = null) → aucune anomalie — INV-VALIDATION-012
- SIREN + pays acheteur valides → aucune anomalie — INV-VALIDATION-012
- SIREN et pays acheteur absents → aucune anomalie (aucune obligation inventée) — INV-VALIDATION-012
- SIREN acheteur invalide (Luhn) → `BUYER_SIREN_INVALID` bloquant — INV-VALIDATION-012
- Pays acheteur non ISO 3166-1 alpha-2 (ZZ, « France », xk) → `BUYER_COUNTRY_INVALID` bloquant — INV-VALIDATION-012
- SIREN ET pays invalides → deux anomalies bloquantes — INV-VALIDATION-012
- Contexte `null` rejeté (`ArgumentNullException`)

### LineTotalsRuleTests (VAL03)
- Document cohérent (Σ lignes HT/TVA = totaux) → aucune anomalie — INV-VALIDATION-013
- Σ lignes HT ≠ total HT → BLOQUANT `DOC_TOTAL_MISMATCH` — INV-VALIDATION-013
- Σ lignes TVA ≠ total TVA → BLOQUANT `DOC_VAT_TOTAL_MISMATCH` — INV-VALIDATION-013
- Écart d'arrondi d'un centime → BLOQUANT (aucune tolérance, EN 16931) — INV-VALIDATION-013
- Arrondi half-up appliqué à la somme des lignes (1160,005 → 1160,01) → cohérent — INV-VALIDATION-013
- Document sans ligne → ignoré par cette règle (blocage porté par StructureRule) — INV-VALIDATION-013
- Charge document incluse dans la réconciliation HT (BR-CO-13) → cohérent — INV-VALIDATION-013
- Remise document incluse dans la réconciliation HT (BR-CO-13) → cohérent — INV-VALIDATION-013
- Écart HT avec charge document → BLOQUANT (réconciliation stricte) — INV-VALIDATION-013
- Réconciliation TVA court-circuitée si charge/remise document présente (TVA non résolue avant TVA04) → pas de faux positif — INV-VALIDATION-013

### ArithmeticRuleTests (VAL03)
- TTC = HT + TVA → aucune anomalie — INV-VALIDATION-013
- TTC ≠ HT + TVA (BR-CO-15) → BLOQUANT `DOC_ARITHMETIC_MISMATCH` — INV-VALIDATION-013
- Écart d'arrondi d'un centime → BLOQUANT (tolérance 0) — INV-VALIDATION-013

### SourceTotalsRuleTests (VAL03)
- Pas de total source (`null`) → aucune anomalie — INV-VALIDATION-014
- Total source = total passerelle → aucune anomalie — INV-VALIDATION-014
- Total source ≠ total passerelle → ALERTE `DOC_TOTAL_SOURCE_MISMATCH` (Warning, n'invalide pas) — INV-VALIDATION-014

### StructureRuleTests (VAL03)
- Document cohérent (1 ligne, date 2024, EUR) → aucune anomalie — INV-VALIDATION-015
- Aucune ligne → BLOQUANT `DOC_NO_LINES` — INV-VALIDATION-015
- Date dans le futur → BLOQUANT `DOC_DATE_FUTURE` (horloge injectée) — INV-VALIDATION-015, INV-VALIDATION-016
- Date du jour / J+1 → pas considérée comme future (marge de fuseau) — INV-VALIDATION-016
- Date antérieure à 2000 → ALERTE `DOC_DATE_TOO_OLD` — INV-VALIDATION-015
- Devise hors ISO 4217 → BLOQUANT `DOC_CURRENCY_INVALID` — INV-VALIDATION-015
- Devise vide → BLOQUANT `DOC_CURRENCY_INVALID` — INV-VALIDATION-015
- Devise en minuscules (« eur ») → acceptée (insensible à la casse) — INV-VALIDATION-015

### UniquenessRuleTests (VAL03)
- Numéro unique (non émis) → aucune anomalie — INV-VALIDATION-017
- Numéro déjà émis → BLOQUANT `DOC_NUMBER_DUPLICATE` — INV-VALIDATION-017
- Numéro absent → BLOQUANT `DOC_NUMBER_MISSING` ET le module Documents n'est pas interrogé — INV-VALIDATION-017
- La recherche est tenant-scopée (CompanyId du contexte) via le port `IIssuedDocumentLookup` — INV-VALIDATION-018

### Iso4217CurrenciesTests (VAL03)
- Codes ISO 4217 valides (EUR, USD, GBP, XOF, casse indifférente) → acceptés — INV-VALIDATION-015
- Codes inconnus, mal formés ou vides (ZZZ, XYZ, EU, EURO, vide, espaces, null) → rejetés — INV-VALIDATION-015

## Integration Tests

Aucun pour VAL01-VAL03 : le framework et les règles de cohérence sont en mémoire. VAL03 interroge le
module Documents via le port `IIssuedDocumentLookup` mais avec un **faux d'essai** (acceptance VAL03) ;
aucune base ni dépendance externe. Les tests d'intégration apparaîtront avec l'implémentation réelle du
port (module Documents, lot TRK — TRK03) et la persistance des anomalies avec le document.
