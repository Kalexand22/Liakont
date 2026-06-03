# ADR-0008: API Versioning — Asp.Versioning.Http

**Date:** 2026-03-27

**Status:** Accepted

---

## Context

Stratum exposes REST endpoints via ASP.NET minimal APIs under the `/api/` prefix.
As the product approaches production, a versioning strategy is needed so that future
breaking changes can coexist with stable contracts. The socle blueprint mandates all
endpoints be prefixed `/api/v1/...` (SOC_I01).

---

## Options Considered

### Option A — Manual `/api/v1/` prefix (no package)
- Pros: zero dependencies, simple string change.
- Cons: no runtime version negotiation, no `api-supported-versions` header,
  no infrastructure for version coexistence — must reinvent everything later.

### Option B — Asp.Versioning.Http (chosen)
- Microsoft-maintained package for minimal API versioning.
- URL-segment reader: `/api/v{version:apiVersion}/...`.
- Reports supported/deprecated versions in response headers automatically.
- Adds `ApiVersionSet` that can be shared across route groups.
- Well-documented migration path to multi-version when needed.

---

## Decision

**Option B** is adopted. Package: `Asp.Versioning.Http` version 8.1.1.

- Version strategy: **URL segment** (`/api/v1/`, `/api/v2/`).
- Default version: **1.0** (assumed when unspecified).
- A single `ApiVersionSet` is created in `AppBootstrap` and shared via a
  top-level route group `/api/v{version:apiVersion}`.
- All module `MapXxxEndpoints()` methods receive this versioned group as their
  `IEndpointRouteBuilder`, removing the `/api` prefix from their local routes.

### Backward Compatibility

Stratum is pre-release. No external consumers depend on `/api/` paths today.
All clients (smoke tests, E2E tests) are updated in the same commit.
No backward-compatibility shim is needed.

---

## Consequences

- **Positive:** future API versions can be introduced by adding a new `ApiVersionSet`
  entry and a `v2` route group without modifying existing v1 endpoints.
- **Positive:** response headers (`api-supported-versions`) inform clients automatically.
- **Negative:** one new NuGet dependency (Microsoft-maintained, MIT license).
