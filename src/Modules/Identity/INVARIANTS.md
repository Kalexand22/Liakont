# Identity — Business Invariants

Domain-level invariants (003, 004, 007) throw on violation.
Handler/DB-level invariants (001, 002, 006) are enforced in command handlers or by DB constraints.
Authentication invariants (005, 008, 010-015) are now managed by Keycloak (removed in KC11).

---

## INV-IDENTITY-001: Unique username

**Rule:** No two active user accounts may share the same username.
**Enforced in:** `CreateUserHandler` + DB unique constraint on `identity.users.username`
**Tested in:** `Stratum.Modules.Identity.Tests.Integration.PostgresUserRepositoryTests`

---

## INV-IDENTITY-002: Unique email

**Rule:** No two active user accounts may share the same email address.
**Enforced in:** `CreateUserHandler` + DB unique constraint on `identity.users.email`
**Tested in:** `Stratum.Modules.Identity.Tests.Integration.PostgresUserRepositoryTests`

---

## INV-IDENTITY-003: No duplicate role on a user

**Rule:** A user cannot be assigned the same role twice.
**Enforced in:** `User.AssignRole` (`Domain/Entities/User.cs`)
**Tested in:** `Stratum.Modules.Identity.Tests.Unit.IdentityInvariantTests`

---

## INV-IDENTITY-004: System roles cannot be renamed or deleted

**Rule:** Roles marked `IsSystem = true` cannot be renamed.
**Enforced in:** `Role.Rename` (`Domain/Entities/Role.cs`)
**Tested in:** `Stratum.Modules.Identity.Tests.Unit.IdentityInvariantTests`

---

## INV-IDENTITY-005: Deactivated user cannot authenticate

**Rule:** Authentication for a deactivated user always fails.
**Enforced in:** Keycloak (user disabled in realm). Locally, `User.IsActive` is checked by authorization handlers.

---

## INV-IDENTITY-006: No duplicate permission per role

**Rule:** The same permission string cannot be granted to the same role twice.
**Enforced in:** DB unique constraint on `(identity.grants.role_id, identity.grants.permission)`
**Tested in:** `Stratum.Modules.Identity.Tests.Integration.PostgresGrantRepositoryTests`

---

## INV-IDENTITY-007: Username format

**Rule:** Username must be 3–50 characters, composed only of alphanumeric characters and underscores.
**Enforced in:** `Username.From` (`Domain/ValueObjects/Username.cs`)
**Tested in:** `Stratum.Modules.Identity.Tests.Unit.ValueObjectTests`

---

## Removed invariants (managed by Keycloak)

The following invariants were enforced by legacy auth code and are now managed by Keycloak:

- **INV-IDENTITY-008:** Password minimum length → Keycloak password policy
- **INV-IDENT-010:** Password must satisfy active policy → Keycloak password policy
- **INV-IDENT-011:** Password must not match recent history → Keycloak password policy
- **INV-IDENT-012:** TOTP code validation → Keycloak MFA
- **INV-IDENT-013:** Expired refresh token rejection → Keycloak session management
- **INV-IDENT-014:** Revoked refresh token rejection → Keycloak session management
- **INV-IDENT-015:** Max concurrent sessions → Keycloak session limits

---

## INV-IDENT-016: Condition syntax must be valid at grant creation

**Rule:** When a grant includes an ABAC condition, its syntax must be parseable (valid operands, supported operators `==`/`!=`).
**Enforced in:** `Grant.Create` via `ConditionParser.Validate` (`Domain/Services/ConditionParser.cs`)
**Tested in:** `Stratum.Modules.Identity.Tests.Unit.ConditionParserTests`

---

## INV-IDENT-017: Invalid condition evaluation = deny

**Rule:** If an ABAC condition fails to evaluate at runtime (missing fields, malformed expression), the result is `false` (deny). No exception is thrown.
**Enforced in:** `ConditionParser.Evaluate` (`Domain/Services/ConditionParser.cs`)
**Tested in:** `Stratum.Modules.Identity.Tests.Unit.ConditionParserTests`

---

## INV-IDENT-018: User preferences theme allow-list

**Rule:** `identity.user_preferences.theme` must be one of `light`, `dark`, or `system`.
**Enforced in:** `PostgresUserPreferencesService.Validate` (`Infrastructure/Services/PostgresUserPreferencesService.cs`) + DB `CHECK (theme IN ('light','dark','system'))` constraint on `identity.user_preferences`.
**Tested in:** `Stratum.Modules.Identity.Tests.Integration.PostgresUserPreferencesServiceTests`

---

## INV-IDENT-019: User preferences density allow-list

**Rule:** `identity.user_preferences.density` must be one of `compact` or `standard`.
**Enforced in:** `PostgresUserPreferencesService.Validate` (`Infrastructure/Services/PostgresUserPreferencesService.cs`) + DB `CHECK (density IN ('compact','standard'))` constraint on `identity.user_preferences`.
**Tested in:** `Stratum.Modules.Identity.Tests.Integration.PostgresUserPreferencesServiceTests`
