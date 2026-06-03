# Audit Module — Invariants

| ID | Rule | Enforcement |
|---|---|---|
| INV-AUDIT-001 | Audit entries are immutable — no UPDATE/DELETE on `audit.field_changes` | Convention: module exposes read-only queries only |
| INV-AUDIT-002 | Audit write failure must not fail business transaction | Enforced in `AuditWriter` (Common.Infrastructure) |
| INV-AUDIT-003 | `entity_type` in policies table is unique | Database constraint `uq_audit_policies_entity_type` + domain validation |
