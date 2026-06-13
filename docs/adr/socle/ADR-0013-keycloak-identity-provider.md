# ADR-0013: Keycloak as External Identity Provider

**Date:** 2026-03-31

**Status:** Accepted

> **Note Liakont ([ADR-0019](../ADR-0019-realm-keycloak-unique-isolation-par-claim.md), 2026-06-13)** :
> le mapping realm↔tenant laissé ouvert ici (« 1:1 vs shared realm with attributes — a deployment
> decision, not a code decision ») est tranché pour Liakont par ADR-0019 : **realm unique partagé
> `liakont`**, isolation des tenants par claim `company_id` (et non par realm).

---

## Context

Stratum's Identity module was built from scratch (Identity-lite) as a deliberate MVP choice (see `docs/mvp-limits.md`). The documented re-evaluation trigger was: *"Quand coût maintenance dépasse coût intégration Keycloak."*

That trigger is now met. The project needs to support multiple authentication sources:

1. **Enterprise accounts** — Office 365 (Azure AD) and Google Workspace for B2B customers.
2. **Personal/social accounts** — Google, Facebook, GitHub for an association-oriented project.
3. **Local accounts** — traditional username/password for internal users or offline scenarios.

Implementing OAuth2/OIDC flows, token refresh, account linking, claim mapping, and provider-specific quirks for each of these is estimated at several months of custom development with ongoing maintenance burden when providers change their APIs.

**Current state:**
- Authentication: custom JWT (HMAC-SHA256) + cookie, login form, PBKDF2 password hashing.
- MFA: TOTP implemented in-house.
- Session management: custom refresh token rotation, max 5 sessions.
- Authorization: custom RBAC with permission-based policies (`PermissionPolicyProvider`).
- Abstraction: `IActorContext` isolates all downstream code from auth implementation details.

**Constraints:**
- Self-hosted mandatory (ERP = sensitive enterprise data).
- Must support multiple isolated contexts (enterprise tenants + association users).
- Must preserve existing RBAC/permission model (business logic stays in Stratum).
- Must integrate with Blazor Server (OIDC redirect flow, not implicit/SPA flow).
- Must work alongside multi-tenant architecture (ADR-0011).

---

## Decision

### Adopt Keycloak as the external identity provider for authentication

**Keycloak** (CNCF project, Apache 2.0) handles *who you are*. Stratum's Identity module keeps *what you can do*.

### Responsibility split

| Concern | Owner | Details |
|---------|-------|---------|
| Authentication (login, password, MFA) | Keycloak | All login flows, password policies, brute-force protection |
| External providers (O365, Google, GitHub, Facebook) | Keycloak | Identity brokering, account linking, claim mapping |
| Session management | Keycloak | SSO sessions, token refresh, session limits |
| User provisioning | Keycloak → Stratum | First-login auto-creates domain User via OIDC claims |
| Roles & permissions (RBAC) | Stratum | `Role`, `Grant`, `PermissionPolicyProvider` unchanged |
| UI rules, field-level auth | Stratum | ABAC conditions (future SOC_S04) unchanged |
| Tenant isolation | Both | Keycloak Realms map to Stratum tenants |

### Architecture

```
Browser → Keycloak login page → OIDC callback → ASP.NET OIDC middleware
  → HttpActorContextAccessor reads JWT claims → IActorContext populated
  → PermissionPolicyProvider checks Stratum's Grant table (unchanged)
```

### Multi-context via Keycloak Realms

- **Enterprise Realm**: Azure AD and Google Workspace as identity brokers. One realm per tenant or shared realm with `organization` attribute.
- **Association Realm**: Google (personal), Facebook, GitHub as social login providers. Shared realm for all association users.
- **Internal Realm** (optional): local accounts for system administrators.

### Token flow

1. Keycloak issues standard OIDC JWT (RS256, asymmetric keys).
2. ASP.NET validates with Keycloak's public key (JWKS endpoint) — no shared secret.
3. Claims mapped: `sub` → external ID, `email`, `preferred_username`, `realm_access.roles` (Keycloak roles, optional).
4. `HttpActorContextAccessor` extracts claims into `IActorContext` (same interface, different source).
5. On first login: `UserSyncService` creates or links a Stratum `User` entity from OIDC claims.

### What gets removed from Stratum

- `JwtTokenGenerator` (Keycloak issues tokens)
- `Rfc2898PasswordHasher` (Keycloak handles passwords)
- `AuthenticateCommand` / `AuthenticateHandler` (Keycloak handles login)
- `RefreshTokenCommand` (Keycloak handles refresh)
- `SetupMfaCommand` / `VerifyMfaCommand` (Keycloak handles MFA)
- `Session` entity and session management (Keycloak handles sessions)
- `PasswordPolicy` entity (Keycloak handles password policies)
- Login.razor form (replaced by Keycloak redirect)

### What stays in Stratum

- `User` aggregate (linked to Keycloak `sub` claim)
- `Role`, `Grant` entities and RBAC logic
- `PermissionPolicyProvider` + `PermissionAuthorizationHandler`
- `HttpActorContextAccessor` (adapted to read OIDC claims)
- `IActorContext` interface (unchanged)
- All module-level authorization checks

---

## Alternatives Considered

### ASP.NET Core Identity + NuGet OAuth packages

- Pro: No external service dependency.
- Con: Each provider is a separate NuGet package to maintain. Account linking, session management, MFA all remain custom code. No admin UI.

### Auth0 / Entra ID (SaaS)

- Pro: Zero infrastructure.
- Con: Per-user pricing at scale. Data sovereignty concerns for ERP. Vendor lock-in. Not self-hosted.

### Duende IdentityServer

- Pro: .NET-native, powerful.
- Con: Commercial license required (non-trivial cost). More complex than needed — Keycloak provides a superset of features with admin UI included.

### Keep Identity-lite, add providers incrementally

- Pro: No migration effort.
- Con: Exponential maintenance burden. Each provider adds ~500-1000 lines of custom OAuth code plus ongoing API change tracking. MFA, session management, password policy all remain custom. This is the path that triggered the re-evaluation.

---

## Consequences

### Positive
- **Zero custom OAuth code** — Keycloak manages all provider integrations.
- **Battle-tested security** — brute-force protection, session management, MFA (TOTP, WebAuthn, SMS) out of the box.
- **Admin UI** — user management, provider configuration, session monitoring without custom development.
- **Multi-context** — Realms provide clean isolation between enterprise and association use cases.
- **Standard compliance** — OIDC/OAuth2 standard, no proprietary token format.
- **Reduced attack surface** — auth code is the most security-critical code; delegating to a CNCF project with thousands of contributors reduces risk.

### Negative
- **Infrastructure dependency** — Keycloak requires a PostgreSQL database and JVM runtime (Docker simplifies this).
- **Migration effort** — existing users need migration, login flow changes, E2E tests need updating.
- **Dev environment complexity** — Docker Compose for Keycloak in dev (mitigated by tooling item KC02).
- **Learning curve** — Keycloak admin concepts (Realms, Clients, Identity Brokering).

### Neutral
- **Stratum's RBAC is unchanged** — downstream modules see the same `IActorContext` and permission model.
- **Multi-tenant integration** — Keycloak Realms can map to Stratum tenants, but the exact mapping (1:1 vs shared realm with attributes) is a deployment decision, not a code decision.

---

## References

- Keycloak documentation: https://www.keycloak.org/documentation
- OIDC Discovery: `/.well-known/openid-configuration`
- ASP.NET Core OIDC middleware: `Microsoft.AspNetCore.Authentication.OpenIdConnect`
- Current Identity module: `src/Modules/Identity/`
- MVP limits trigger: `docs/mvp-limits.md` — "Identity-lite vs provider externe"
- IActorContext: `src/Common/Abstractions/Security/IActorContext.cs`
