# Identity — Business Scenarios

Key business scenarios for the Identity module.

---

## SC-IDENTITY-001: Create user and authenticate

**Given** a new operator needs access to the ERP system
**When** a `CreateUserCommand` is issued with a valid username, email, and password
  and an `AuthenticateCommand` is issued with those credentials
**Then** the user exists with `IsActive = true`
  and `AuthResultDto.IsSuccess` is `true`
  and a non-empty JWT token is returned
  and a `UserCreatedV1` and `UserAuthenticatedV1` event are in the outbox

**Relevant invariants:** INV-IDENTITY-007, INV-IDENTITY-008

---

## SC-IDENTITY-002: Deactivate user and verify authentication blocked

**Given** an active user account
**When** a `DeactivateUserCommand` is issued
  and an `AuthenticateCommand` is issued for the same user
**Then** `AuthResultDto.IsSuccess` is `false`
  and the error message indicates the account is deactivated (INV-IDENTITY-005)
  and a `UserDeactivatedV1` event is in the outbox

**Relevant invariants:** INV-IDENTITY-005

---

## SC-IDENTITY-003: Assign role and check permissions

**Given** an active user and an existing role with permissions granted
**When** an `AssignUserRoleCommand` is issued
  and `IIdentityQueries.UserHasPermission` is called
**Then** the permission check returns `true`
  and a `UserRoleAssignedV1` event is in the outbox
**When** a `RevokeUserRoleCommand` is issued
**Then** `IIdentityQueries.UserHasPermission` returns `false`

**Relevant invariants:** INV-IDENTITY-003

---

## SC-IDENTITY-004: Username and password validation at creation

**Given** invalid input data
**When** a `CreateUserCommand` is issued with a username shorter than 3 chars
**Then** `User.Create` throws with `INV-IDENTITY-007`
**When** a `CreateUserCommand` is issued with a password shorter than 8 chars
**Then** `User.Create` throws with `INV-IDENTITY-008`

**Relevant invariants:** INV-IDENTITY-007, INV-IDENTITY-008

---

## SC-IDENTITY-005: System role protection

**Given** a role created with `IsSystem = true`
**When** `Role.Rename` is called on it
**Then** an `InvalidOperationException` is thrown with `INV-IDENTITY-004`

**Relevant invariants:** INV-IDENTITY-004
