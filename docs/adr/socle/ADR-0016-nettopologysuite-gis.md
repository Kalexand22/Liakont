# ADR-0016: NetTopologySuite for GIS Spatial Operations

**Date:** 2026-04-08

**Status:** Proposed

---

## Context

Stratum's Reservation module requires GIS capabilities for spatial conflict detection between municipal resources (parking zones, roads, event areas). The GIS Integration layer (lot GIS, ADR-0015 "Moteur 5") needs:

- GeoJSON parsing and serialization (RFC 7946)
- Geometry validation
- Spatial operations: intersection, containment, area calculation, bounding box
- Spatial conflict detection between overlapping geometries

These operations require a computational geometry library. .NET has no built-in spatial support beyond basic math.

**Constraints:**

- Must be MIT/Apache licensed (open-source compatible).
- Must support GeoJSON (RFC 7946) as the interchange format.
- Must integrate with PostgreSQL PostGIS for future persistence (optional, not required in Phase 1).
- Must not pull in heavy transitive dependencies.
- Must be mature and well-maintained.

---

## Decision

Add **NetTopologySuite** (NTS) as an allowed dependency for `Common.Infrastructure`.

### Packages

| Package | Version | Purpose | Target |
|---------|---------|---------|--------|
| `NetTopologySuite` | 2.6.0 | Core geometry engine (JTS port) | `Common.Infrastructure` |
| `NetTopologySuite.IO.GeoJSON4STJ` | 4.0.0 | GeoJSON ↔ NTS geometry conversion (System.Text.Json) | `Common.Infrastructure` |

### Rationale

- **De facto standard**: NTS is the .NET port of JTS (Java Topology Suite), the industry standard for computational geometry. Used by EF Core Spatial, PostGIS .NET drivers, and most .NET GIS projects.
- **Mature**: 20+ years of development (JTS since 2002, NTS since 2004). Stable API.
- **Lightweight**: Core package has zero external dependencies. GeoJSON2 depends only on System.Text.Json.
- **PostGIS ready**: NTS types map directly to PostgreSQL geometry types via Npgsql.NetTopologySuite (future Phase 2+).
- **MIT licensed**: No commercial restrictions.

### Usage boundaries

- NTS types (`Geometry`, `Point`, `Polygon`, etc.) are **internal to `Common.Infrastructure.Gis`**.
- Public interfaces in `Common.Abstractions.Gis` use Stratum value objects (`GeoJsonGeometry`) — a thin wrapper around raw GeoJSON strings.
- No module may reference NTS directly. All spatial operations go through `IGeoJsonService` and `ISpatialConflictDetector`.

---

## Rejected Alternatives

| Alternative | Reason |
|-------------|--------|
| Manual geometry math | Error-prone, no GeoJSON support, no standards compliance |
| GeoJSON.Net | Only handles serialization, no spatial operations (intersection, area) |
| GDAL/OGR bindings | Heavy native dependency, complex deployment, overkill for our needs |
| PostGIS-only (SQL) | Couples spatial logic to the database, breaks the domain layer |

---

## Consequences

**Positive:**
- Standards-compliant GeoJSON handling out of the box
- Reliable spatial conflict detection (intersection, containment)
- Future-proof: PostGIS integration via Npgsql.NetTopologySuite when needed
- Well-documented API with extensive test coverage upstream

**Negative:**
- New transitive dependency in Common.Infrastructure (2 packages)
- NTS API uses mutable geometry types — must ensure immutability at the abstraction boundary

**Mitigation:**
- Encapsulate all NTS usage behind `IGeoJsonService` / `ISpatialConflictDetector` interfaces
- Architecture tests verify no module references NTS directly
