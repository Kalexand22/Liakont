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

### AuctionVerticalSettingsTests (FIX03)
- `CreateDefault` désactive le vertical enchères (défaut OFF, D4) — INV-TENANTSETTINGS-010
- `Create` persiste l'état demandé (activé / désactivé)
- `Update` bascule l'état et pose `updated_at`

### TenantSettingsConsoleQueriesTests (API01c)
- Vue VIDE tant que le profil du tenant n'existe pas (companyId null) — état transitoire, jamais une erreur
- Projection de l'état de la table TVA (version, validateur, validation, comportement par défaut, nombre de règles)
- Capacités déclarées exposées pour un compte PA dont le plug-in est chargé (jamais inventées) — INV-TENANTSETTINGS-009
- Type de PA sans plug-in chargé signalé indisponible (capacités `null`), sans erreur — INV-TENANTSETTINGS-009
- Le compte PA est passé déjà masqué (`HasApiKey` seul) — INV-TENANTSETTINGS-003

## Integration Tests (Tests.Integration/ — PostgreSQL Testcontainers)

### TenantProfileIntegrationTests
- Upsert profil : round-trip create puis update
- Isolation : deux company_id distincts ne se voient pas — INV-TENANTSETTINGS-006

### FiscalSettingsIntegrationTests
- Upsert fiscal avec paramètres `null` persistés et relus `null` — INV-TENANTSETTINGS-004

### AuctionVerticalSettingsIntegrationTests (FIX03)
- Ligne absente ⇒ lecture `false` (défaut OFF) — INV-TENANTSETTINGS-010
- Activation puis désactivation : round-trip + journalisation `created`/`updated` — INV-TENANTSETTINGS-005/010
- Activation d'un tenant sans effet sur un autre (tenant-scopé) — INV-TENANTSETTINGS-006

### PaAccountIntegrationTests
- La clé API est chiffrée en base (la colonne ne contient pas le clair) — INV-TENANTSETTINGS-003
- Le DTO de lecture n'expose jamais la clé (`HasApiKey` seulement) — INV-TENANTSETTINGS-003
- Déchiffrement round-trip via `ISecretProtector`
- Désactivation d'un compte
- Doublon (tenant, plug-in, environnement) rejeté avec `ConflictException` — F12-A §4

### Handler behaviours (covered by integration tests)
- `AddPaAccountHandler` : la clé API en clair est chiffrée (jamais persistée telle quelle) ;
  l'entité ne contient pas le clair — INV-TENANTSETTINGS-003
- `SaveTenantProfileHandler` : journalise la mutation avec l'identité opérateur — INV-TENANTSETTINGS-005
- `SaveTenantProfileHandler` : changement de SIREN sur profil existant rejeté — INV-TENANTSETTINGS-001

### SeedImportIntegrationTests
- Import idempotent (rejouable) du profil + fiscal + planification + seuils — F12-A §8.2
- La clé API d'un compte PA n'est jamais importée (placeholder, avertissement) — INV-TENANTSETTINGS-007

### JournalingIntegrationTests
- Une mutation produit une entrée d'activité avec l'identité opérateur — INV-TENANTSETTINGS-005

## Console API Tests (tests/Liakont.Console.Api.Tests.Integration — in-process HTTP + PostgreSQL)

### TenantSettingsEndpointsIntegrationTests (API01c — GET /api/v1/settings)
- 401 sans authentification, 403 sans permission `liakont.read`
- Lecture : profil + fiscal + état de la table TVA (validée) du tenant
- Secrets masqués : la clé chiffrée n'est jamais sérialisée, seul `HasApiKey` est exposé — INV-TENANTSETTINGS-003
- Capacités PA déclarées exposées pour un compte dont le plug-in (Fake) est chargé — INV-TENANTSETTINGS-009
- Type de PA non enregistré signalé indisponible (capacités `null`), sans erreur — INV-TENANTSETTINGS-009
- Vue vide (200) tant que le tenant n'est pas paramétré
- Isolation tenant : A ≠ B (profil, comptes PA, table TVA) — INV-TENANTSETTINGS-006
