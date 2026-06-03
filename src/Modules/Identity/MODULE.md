# Identity Module

## Purpose

The Identity module manages user accounts, authentication, roles, and permissions.
It provides the security backbone for the ERP platform: any actor performing a command
must be identifiable through a user account issued by this module.

## Bounded Context

**Inside:**
- User aggregate (username, email, password hash, active status, optional Party link)
- Role entity (named groups of permissions; system roles are protected)
- Grant entity (permission string bound to a role)
- User-role assignment (many-to-many)
- JWT token generation (via `IJwtTokenGenerator`, implemented in Host)
- All write commands and corresponding integration events

**Outside:**
- Party master data (→ Party module; optional link via `PartyId` on User)
- Authorisation middleware and token validation (→ Host)
- Transactional documents referencing users (→ Sales, Purchasing)

## Owner

ERP Platform Team

## Integration Events

### Published

| Event | Trigger |
|---|---|
| `identity.user.created` (`UserCreatedV1`) | User account created |
| `identity.user.deactivated` (`UserDeactivatedV1`) | User account deactivated |
| `identity.user.authenticated` (`UserAuthenticatedV1`) | User successfully authenticated |
| `identity.user_role.assigned` (`UserRoleAssignedV1`) | Role assigned to user |

All events are published via the transactional outbox (`outbox.pending_events`).

### Consumed

None. Identity has no upstream event dependencies.

## Cross-Module API

Other modules consume `IIdentityQueries` from `Stratum.Modules.Identity.Contracts`:

| Method | Purpose |
|---|---|
| `GetUserById` | Get user DTO by ID |
| `GetUserByUsername` | Get user DTO by username |
| `GetUserPermissions` | Get all permission strings for a user (via roles) |
| `UserHasPermission` | Check if user holds a specific permission |
| `GetRoles` | List all roles |

## Database Schema

Tables live in the `identity` schema:

| Table | Description |
|---|---|
| `identity.users` | User accounts |
| `identity.roles` | Named roles |
| `identity.user_roles` | User ↔ Role assignment |
| `identity.grants` | Permission strings bound to roles |
| `identity.user_preferences` | Per-user UI preferences (theme, language, density, extensions JSON) |
