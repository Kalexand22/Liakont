# FIX01a — Provisioning tenant (CFG02) : import de seed câblé + état « paramétrage incomplet » visible

Bug-inbox P1 : profil tenant (CFG02) sans aucun chemin de création → docs Detected + dead-letter
silencieux. Décision opérateur D1 : DevTenantSeeder (dev) + endpoint admin SystemAdmin (prod).

## Architecture tranchée

- `ImportTenantSeedCommand`+handler existent mais jamais câblés hors tests → on les CÂBLE.
- Le companyId du 1er profil ne peut venir ni de la DB (pas de profil) ni de l'actor (pas d'HTTP
  user du tenant cible) → champ `CompanyId?` explicite sur la commande (fallback `ICompanyFilter`
  pour un éventuel chemin HTTP). En dev = company_id hardcodé du realm
  (00000000-0000-4000-a000-000000000001, cf. deploy/docker/keycloak/realm-export.json).
- Exécution hors-HTTP via `ITenantScopeFactory.Create(tenantId)` + `ISender.Send` (behaviors gated ;
  TenantPropagationBehavior OK car actor.TenantId == tenantContext.TenantId — même MutableTenantContext).

## Tâches

### A. Commande + handler (module TenantSettings)
- [ ] `ImportTenantSeedCommand` : ajouter `Guid? CompanyId`.
- [ ] `ImportTenantSeedHandler` : `companyId = request.CompanyId ?? _companyFilter.GetRequiredCompanyId()`.

### B. Seeder dev (Host) — après MigrateExistingTenantsAsync (schéma tenant créé là)
- [ ] `DevTenantSeedOptions` : `Guid CompanyId` + `string? SeedDirectoryPath`.
- [ ] `DevTenantSeeder.SeedDevTenantProfileAsync` : scope tenant → ISender.Send(import) ; non fatal ;
      chemin résolu vs ContentRoot ; skip+warn si CompanyId absent ou dossier introuvable.
- [ ] `AppBootstrap.InitializeDataAsync` : appel après migrations.
- [ ] `appsettings.Development.json` : CompanyId + SeedDirectoryPath.

### C. Endpoint admin (Host)
- [ ] `TenantAdminEndpointMapping` : `POST /{tenantId}/seed` (SystemAdmin) → scope + import.
      404 tenant inconnu, 400 CompanyId/path manquant. Aucun secret écrit (INV-TENANTSETTINGS-007).

### D. UI « paramétrage incomplet — traitement suspendu »
- [ ] `DashboardViewModel.ProfileConfigured` + `DashboardQueryService` + bandeau `DashboardView`.
- [ ] Bandeau en tête de `ParametrageView` quand Profile null.

### E. Tests (EXÉCUTÉS)
- [ ] `SeedImportIntegrationTests` : import avec CompanyId explicite (sans actor) → profil+fiscal+seuils, aucun secret.
- [ ] bUnit `DashboardViewTests` + `ParametrageViewTests` : bandeau suspendu quand profil absent / absent quand présent.
- [ ] `TenantSeedAdminEndpointTests` (Console.Api.Tests.Integration) : 401/403/404/200 ; profil créé visible via GET /settings ;
      harness +SystemAdmin role (X-Test-Roles) +tenant vide dédié.

### F. Docs
- [ ] Note rejeu dead-letter CFG02 (D #3 = documentation du rejeu) + état suspendu (README seed).

## Review
(à compléter après codex-review)
