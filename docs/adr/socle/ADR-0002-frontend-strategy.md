# ADR-0002: Frontend Strategy — Blazor Web App

**Date:** 2026-03-15

**Status:** Accepted

---

## Context

Stratum requires a web UI to enable manual validation of the Party, Identity-lite, and Sales Quotes Lite modules as part of Lot K. The UI must integrate with the existing modular monolith (C# / .NET 10) and allow developers and validators to exercise the full ERP flow: login → party management → quote creation.

Two secondary constraints drive specific sub-decisions:
1. The existing REST API uses JWT Bearer for authentication. Adding a frontend must not break or degrade API behavior.
2. Blazor components need a browser-side authentication state, which JWT tokens alone cannot provide (no persistent cookie, no server-side session).

---

## Decision

### Render Mode: Interactive Server (default), WASM reserved

Stratum adopts **Blazor Server-Side Rendering with Interactive Server** as the default render mode.

- All pages use `@rendermode InteractiveServer` unless a page explicitly requires static SSR.
- WASM render mode is not used. It is reserved for a future decision if offline-capable mobile-like pages are needed.
- The exception is the **Login page**, which MUST use static SSR (no `@rendermode` directive). HttpContext — required for `HttpContext.SignInAsync` to issue a cookie — is not available inside an interactive server circuit (WebSocket). Login must be a classic form POST handled during static rendering.

### No Business Logic in Razor Components

Blazor components are **presentation only**:
- Components may inject `ISender` (MediatR) for command dispatch and module query interfaces (e.g., `IPartyQueries`) for reads.
- No domain logic, validation rules, or business calculations belong in components.
- Components produce error messages from exceptions thrown by handlers — they do not reimplement business rules.

### Dual Authentication: JWT for REST API, Cookie for Blazor Circuit

Two authentication schemes are configured:

| Scheme | Constant | Purpose |
|--------|----------|---------|
| JWT Bearer | `JwtBearerDefaults.AuthenticationScheme` ("Bearer") | Default scheme. Used for all REST API endpoints. Returns 401 on unauthorized. |
| Cookie | `CookieAuthenticationDefaults.AuthenticationScheme` ("Cookies") | Named scheme only. Used exclusively for the Blazor circuit. Issues a session cookie on login. |

**JWT Bearer is the default authenticate and challenge scheme.** This is critical: if Cookie were the default, unauthorized REST API calls would receive a 302 redirect to `/login` instead of a 401 response, breaking API consumers.

The Cookie scheme is wired into Blazor components for `HttpContext.SignInAsync` / `SignOutAsync` on the Login page (static SSR only). The Blazor circuit uses `CascadingAuthenticationState` backed by the cookie to determine user identity.

---

## Rejected Alternatives

### TypeScript / React Frontend

Rejected — see ADR-0001. Polyglot stack increases context switching and breaks type safety at API boundaries.

### WASM Render Mode (default)

Rejected for initial implementation:
- Requires downloading the .NET runtime to the browser (~10 MB).
- Adds complexity for authentication state synchronization between server and client.
- Adds build artifacts that slow CI.
- Not necessary for an internal validation UI.

### Cookie as Default Auth Scheme

Rejected because it would cause REST API endpoints to return 302 redirects (cookie challenge behavior) instead of 401 responses. This breaks all API consumers (tests, external tools, mobile clients).

### Separate Blazor Project

Rejected — a separate project increases deployment complexity and adds a network boundary between the UI and the application layer. Blazor in the same Host process benefits from in-process calls to MediatR handlers and module query interfaces.

---

## Consequences

1. **Login page must always use static SSR.** Any future login-adjacent pages that need `HttpContext` access must also avoid `@rendermode`.
2. **Cookie scheme is not the default.** Components that need to sign in/out must explicitly pass `CookieAuthenticationDefaults.AuthenticationScheme` to `HttpContext.SignInAsync`.
3. **REST API behavior is unchanged.** Existing JWT-protected endpoints continue to return 401 on unauthorized access.
4. **WASM migration path is open.** No architecture change prevents migrating specific pages to WASM in a future ADR.
