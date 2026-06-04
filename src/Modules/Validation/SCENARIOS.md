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
- Clé cohérente avec le SIREN (FR11123456782, FR12000000000) → valide — INV-VALIDATION-009
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

## Integration Tests

Aucun pour VAL01 : le framework est en mémoire (aucune base, aucune dépendance externe). Les tests
d'intégration apparaîtront avec les règles consommant d'autres modules (VAL03/VAL04) et la persistance
des anomalies avec le document (module Documents, lot TRK).
