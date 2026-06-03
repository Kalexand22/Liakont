# Audit Module — Test Scenarios

## Unit Tests (Tests.Unit/AuditPolicyTests.cs)

- Create policy with valid inputs
- INV-AUDIT-003: empty entity_type rejected
- Empty module_source rejected
- Empty tracked fields accepted
- Update changes fields and sets updated_at
- Disable sets is_enabled to false
- Reconstitute maps all fields

## Integration Tests (Tests.Integration/)

### AuditPolicyRepositoryTests

- Insert and GetByEntityType round-trips
- Update persists changes
- GetByEntityType not found returns null
- INV-AUDIT-003: duplicate entity_type rejected by database constraint

### AuditQueriesTests

- GetAuditPolicies returns list
- GetPolicyByEntityType found returns DTO
- GetPolicyByEntityType not found returns null
- GetFieldChanges returns inserted changes
- GetFieldChanges pagination (page 1, 2, 3)
- GetFieldChanges no data returns empty
