# Audit Module

## Purpose

Provides audit trail consultation and audit policy management. The module reads field-level change data from the `audit.field_changes` infrastructure table (managed by `Common.Infrastructure` via `IAuditWriter`) and manages audit policies that define which entity types are tracked.

## Boundaries

- **Owns:** `audit_module` schema (policies table)
- **Reads:** `audit` schema (field_changes table — infrastructure, read-only)
- **Does NOT:** Write audit data (that responsibility stays in `Common.Infrastructure.IAuditWriter`)

## Published Events

None. The Audit module is a pure read/config consumer.

## Consumed Events

None. It reads `audit.field_changes` directly via SQL queries.

## Dependencies

- `Common.Abstractions` (MediatR contracts)
- `Common.Infrastructure` (database access, migrations)
- No cross-module dependencies
