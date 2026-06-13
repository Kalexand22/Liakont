# RLM02 — Résolution du tenant pilotée par le jeton (company_id UNIQUE + NOT NULL + résolveur autoritaire)

ADR-0021 §2c, étape 3 du séquencement. Branche `feat/tenant-provisioning-RLM02`.

## Contexte / décisions de conception (vérifiées sur pièce)

- company_id canonique du tenant `default` = `00000000-0000-4000-a000-000000000001`
  (realm-export dev+E2E, appsettings `DevTenantSeed:CompanyId`). Registre `outbox.tenants` = NULL → à backfiller.
- Migration : DbUp, embarquée `Migrations\*.sql`, prochaine = **V017**.
- **V017 AUTO-GARDÉE** (`IF EXISTS outbox.tenants`) plutôt que d'ajouter `V017__` à
  `SystemOnlyMigrationPrefixes` (qui vit dans le fichier socle vendored ÉPINGLÉ
  `TenantProvisioningService.cs`) — garde RLM02 SANS dérive socle (la dérive socle est le périmètre de RLM04).
  No-op sur une base TENANT (qui n'a pas `outbox.tenants`), ALTER réel sur la base SYSTÈME.
- Résolveur lit le claim `company_id` DIRECTEMENT du `ClaimsPrincipal` (jamais `IActorContext` :
  `HttpActorContextAccessor.Build()` lit `tenantContext.TenantId` et MET EN CACHE → un résolveur qui
  lit `IActorContext` figerait un TenantId null pendant la résolution). Le NetArchTest prouve l'absence
  de dépendance Keycloak ; la lecture via `IActorContext` du cross-check est le périmètre de RLM03.
- Lookup `company_id→tenant` = nouveau fichier (NON épinglé) dans `src/Common` (couche query, comme
  `TenantQueries`), requête synchrone Dapper indexée par la contrainte UNIQUE.

## Production

- [ ] `src/Common/Infrastructure/Migrations/V017__enforce_company_id_on_tenants.sql` (auto-gardée : backfill default → NOT NULL → UNIQUE)
- [ ] `src/Common/Abstractions/MultiTenancy/ICompanyTenantLookup.cs` (public, `string? FindTenantId(Guid)`)
- [ ] `src/Common/Infrastructure/Database/CompanyTenantLookup.cs` (public, Dapper sync, `IOptions<DatabaseOptions>`)
- [ ] `src/Host/Liakont.Host/MultiTenancy/CompanyClaimTenantResolver.cs` (ITenantResolver, lit le claim, résout via lookup)
- [ ] `MultiTenantServiceCollectionExtensions.cs` : enregistrer le lookup + le résolveur EN PREMIER (autoritaire) + maj du commentaire d'ordre
- [ ] `DevTenantSeeder.cs` : insert `default` AVEC company_id (`options.CompanyId`) + garde CompanyId non vide (sinon NOT NULL casse le bring-up dev)

## Tests (EXÉCUTÉS, pas seulement écrits)

- [ ] Migration (DatabaseFixture) : company_id NOT NULL + contrainte UNIQUE ; insert doublon → unique_violation (INV-0021-2c) ; insert NULL → not_null_violation
- [ ] Backfill (DbUp 2 passes, base fraîche) : default NULL → ...001
- [ ] Cohérence 3 sources (unit) : realm-export ↔ appsettings DevTenantSeed:CompanyId ↔ UUID backfill V017 coïncident
- [ ] Résolveur (unit, fake lookup) : claim présent→tenant ; absent/malformé→null ; lookup null→null
- [ ] Lookup (DatabaseFixture) : FindTenantId(...001)→'default' ; inconnu→null
- [ ] Ordre autoritaire (unit) : 1er ITenantResolver enregistré = CompanyClaimTenantResolver
- [ ] NetArchTest : `Liakont.Host.MultiTenancy` ne dépend d'aucun type Keycloak (INV-0021-9)

## Vérif
- [ ] verify-fast (plateforme .NET 10 + agent net48)
- [ ] run-tests (intégration)
- [ ] codex-review propre / P2 acceptés justifiés
- [ ] socle-provenance-check vert (aucune dérive socle attendue)
