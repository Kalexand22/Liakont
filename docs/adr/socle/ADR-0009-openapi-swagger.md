# ADR-0009: OpenAPI Documentation — Microsoft.AspNetCore.OpenApi + SwaggerUI

**Date:** 2026-03-27

**Status:** Accepted

---

## Context

Stratum exposes REST endpoints via ASP.NET minimal APIs, versioned under `/api/v1/...`
(ADR-0008). Developers and future API consumers need interactive documentation to
explore endpoints, understand request/response schemas, and test authenticated calls.
The socle blueprint mandates OpenAPI/Swagger support (SOC_I02).

---

## Options Considered

### Option A — Swashbuckle.AspNetCore (all-in-one)
- Pros: single package, well-known.
- Cons: was the .NET default until .NET 8, then dropped from templates.
  Less actively maintained, slower to adopt minimal API features, not the
  Microsoft-recommended path for .NET 9+.

### Option B — Microsoft.AspNetCore.OpenApi + Swashbuckle.AspNetCore.SwaggerUI (chosen)
- `Microsoft.AspNetCore.OpenApi` is the official .NET 10 approach for OpenAPI
  document generation. First-class minimal API support, automatic schema inference,
  built-in document transformers for customisation.
- `Swashbuckle.AspNetCore.SwaggerUI` provides the standalone Swagger UI middleware
  that consumes the generated OpenAPI document.
- Best of both worlds: Microsoft-maintained generation, familiar UI.

### Option C — Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore
- Scalar is a modern alternative UI.
- Rejected: acceptance criteria specify Swagger UI at `/swagger`.

---

## Decision

**Option B** is adopted.

Packages:
- `Microsoft.AspNetCore.OpenApi` version 10.0.0 (Microsoft-maintained, MIT).
- `Swashbuckle.AspNetCore.SwaggerUI` version 7.3.1 (OSS, MIT).

Configuration:
- `AddOpenApi("v1")` with a document transformer that injects the JWT Bearer
  security scheme and sets document title/version.
- `MapOpenApi()` exposes the generated spec at `/openapi/v1.json`.
- `UseSwaggerUI()` serves the Swagger UI at `/swagger`, configured to fetch the
  spec from `/openapi/v1.json`.
- Both `MapOpenApi()` and `UseSwaggerUI()` are gated on
  `IsDevelopment() || IsEnvironment("Test")` — disabled in Production.

---

## Consequences

- **Positive:** zero-touch schema generation from minimal API signatures; no manual
  annotations needed on existing endpoints.
- **Positive:** JWT Bearer auth scheme visible in Swagger UI "Authorize" dialog,
  applied per-operation (skipped on `[AllowAnonymous]` endpoints like login).
- **Positive:** API version `v1` reflected in the document info and endpoint paths.
- **Negative:** two new NuGet dependencies (one Microsoft-maintained, one OSS/MIT).
