# TenantSettings Module — Test Scenarios

## Unit Tests (Tests.Unit/)

### SirenValidatorTests
- SIREN valide (Luhn) accepté — INV-TENANTSETTINGS-001
- SIREN à clé de Luhn invalide rejeté — INV-TENANTSETTINGS-001
- SIREN de longueur ≠ 9 ou non numérique rejeté — INV-TENANTSETTINGS-001

### TenantProfileTests
- Création valide ; statut par défaut Actif
- SIREN invalide rejeté à la création — INV-TENANTSETTINGS-001
- Raison sociale vide / pays non ISO rejetés — INV-TENANTSETTINGS-002
- Suspend/Reactivate changent le statut et posent updated_at

### FiscalSettingsTests
- Tous paramètres `null` acceptés (suspension) — INV-TENANTSETTINGS-004
- `reportingFrequency` stocké tel quel (chaîne opaque), jamais interprété — INV-TENANTSETTINGS-008

### AlertThresholdsTests
- `CreateDefault` applique les défauts F12-A §6
- Seuil non positif rejeté — INV-TENANTSETTINGS-002

### ExtractionScheduleTests
- Heure au mauvais format rejetée — INV-TENANTSETTINGS-002

### Handler Tests (avec fakes)
- `AddPaAccountHandler` : la clé API en clair est chiffrée (jamais persistée telle quelle) ;
  l'entité ne contient pas le clair — INV-TENANTSETTINGS-003
- `SaveTenantProfileHandler` : journalise la mutation avec l'identité opérateur — INV-TENANTSETTINGS-005
- `SaveTenantProfileHandler` : changement de SIREN sur profil existant rejeté — INV-TENANTSETTINGS-001

## Integration Tests (Tests.Integration/ — PostgreSQL Testcontainers)

### TenantProfileIntegrationTests
- Upsert profil : round-trip create puis update
- Isolation : deux company_id distincts ne se voient pas — INV-TENANTSETTINGS-006

### FiscalSettingsIntegrationTests
- Upsert fiscal avec paramètres `null` persistés et relus `null` — INV-TENANTSETTINGS-004

### PaAccountIntegrationTests
- La clé API est chiffrée en base (la colonne ne contient pas le clair) — INV-TENANTSETTINGS-003
- Le DTO de lecture n'expose jamais la clé (`HasApiKey` seulement) — INV-TENANTSETTINGS-003
- Déchiffrement round-trip via `ISecretProtector`
- Désactivation d'un compte

### SeedImportIntegrationTests
- Import idempotent (rejouable) du profil + fiscal + planification + seuils — F12-A §8.2
- La clé API d'un compte PA n'est jamais importée (placeholder, avertissement) — INV-TENANTSETTINGS-007

### JournalingIntegrationTests
- Une mutation produit une entrée d'activité avec l'identité opérateur — INV-TENANTSETTINGS-005
