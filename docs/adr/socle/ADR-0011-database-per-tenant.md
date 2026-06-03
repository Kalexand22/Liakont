# ADR-0011: Database-per-Tenant (supersedes ADR-0010)

**Date:** 2026-03-30

**Status:** Accepted (supersedes ADR-0010)

---

## Context

ADR-0010 chose schema-per-tenant isolation. During implementation review, two issues
emerged:

1. **Conflicting schema models**: Modules already use per-module schemas (`party`,
   `sales`, `company`, etc.). Schema-per-tenant required flattening all module tables
   into a single tenant schema or maintaining dual naming — neither was implemented.

2. **Original requirement was database-per-tenant**: The stakeholder requirement was
   full database isolation for strongest data separation, independent backup/restore,
   and clean per-tenant lifecycle management.

---

## Decision

**Database-per-tenant** (Option A from ADR-0010). Each tenant gets its own PostgreSQL
database (e.g., `stratum_acme`) containing all module schemas (`party`, `sales`, etc.)
exactly as they exist in the system database.

### Connection Routing

- `TenantAwareNpgsqlConnectionFactory` derives the connection string by changing the
  `Database` property: `stratum_{tenantId}`.
- Per-tenant connection string overrides remain supported for tenants hosted on
  different servers.
- System tenant (null) uses the default connection string.

### Provisioning

- `CREATE DATABASE stratum_{tenantId}` on the system PostgreSQL server.
- All module migrations (`.Migrations.`) run in the new database — same as
  `MigrationRunner.MigrateUp` for the system database.
- Tenant registry remains in `outbox.tenants` (system database).

### IConnectionFactory Split

- `IConnectionFactory` is now **scoped** — reads `ITenantContext` to route to the
  correct tenant database. All module repositories use this transparently.
- `ISystemConnectionFactory` is a new **singleton** interface for services that always
  operate on the system database (OutboxWorker, AuditWriter, health checks).

---

## Consequences

- Module repositories require zero changes — they continue using `IConnectionFactory`.
- Singleton background services must use `ISystemConnectionFactory`.
- Each tenant database runs the full migration set independently.
- Per-tenant backup/restore is a standard `pg_dump`/`pg_restore` operation.
- Connection pool fragmentation increases with tenant count — each tenant gets its own
  `NpgsqlDataSource` in the registry.
- ADR-0010 is superseded; its resolution chain design (subdomain/header/JWT) remains
  valid and unchanged.
