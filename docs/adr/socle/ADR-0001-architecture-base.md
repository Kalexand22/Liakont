# ADR-0001: Architecture Base

**Date:** 2026-03-12

**Status:** Accepted

---

## Context

Stratum is an ERP system designed to be developed and maintained by both humans and AI agents. The codebase will grow to encompass modules for sales, customers, invoicing, payments, audit, mobile field operations, and more.

The architecture must satisfy these constraints simultaneously:

1. **Maintainability at scale.** As modules multiply, any single change must remain localized. A developer (human or AI) working on the Sales module must not need to understand the Customer module's internals.
2. **AI-native development.** AI agents participate in code generation, PR creation, and review. The architecture must limit the context window required to modify any part of the system.
3. **Testability.** Every business invariant, workflow, and integration point must be automatically verifiable. Regressions must be caught by CI, not by humans.
4. **Single deployment simplicity.** The team is small. Operational overhead must be minimal. There is no dedicated infrastructure team.
5. **C# end-to-end.** Backend, web UI, and mobile share a single language to eliminate context switching and maximize type safety across boundaries.

---

## Decision

Stratum adopts a **strict modular monolith** architecture with the following locked choices:

- **Language:** C# end-to-end (backend, Blazor Server for web, .NET MAUI for mobile).
- **Runtime:** .NET 10 LTS.
- **Database:** Single PostgreSQL instance. Each module owns a dedicated schema (e.g., `sales.*`, `customer.*`). The `public` schema is not used.
- **Data access:** Dapper with explicit SQL. No ORM.
- **Intra-module messaging:** MediatR.
- **Inter-module communication:** Integration events via an Event Dispatcher, persisted through the outbox pattern.
- **Module isolation:** Modules depend only on each other's Contracts layer (commands, queries, DTOs, events). No access to another module's Domain, Application, Infrastructure, or Web layers. Isolation is enforced by NetArchTest architecture tests that fail the build on violation.
- **Deployment:** Single deployable unit. All modules run in one process.
- **No network communication between modules.** All inter-module calls are in-process. Events are dispatched in-memory after outbox polling.
- **Transactions:** ACID transactions are local to one module. Inter-module consistency is achieved through events, idempotent consumers, and the outbox pattern.

---

## Rejected Alternatives

### Microservices

Rejected because:
- Operational complexity is disproportionate for a small team with no dedicated infrastructure staff.
- Network boundaries between modules introduce latency, partial failure modes, and distributed tracing overhead.
- Deployment of dozens of services requires container orchestration (Kubernetes), service mesh, and distributed logging infrastructure that does not exist.
- The modular monolith preserves the option to extract a module into a service later, because each module already owns its own schema and communicates only through contracts.

### Classic (Unstructured) Monolith

Rejected because:
- No enforced boundaries between domains leads to coupling that grows with codebase size.
- Cross-domain database queries create hidden dependencies that make changes unsafe.
- Refactoring becomes prohibitively expensive once coupling is established.
- AI agents generating code in an unstructured monolith cannot reason about boundaries, leading to quality degradation.

### Polyglot Stack (e.g., TypeScript frontend + C# backend)

Rejected because:
- Context switching between languages increases cognitive load for both humans and AI agents.
- Type safety breaks at the language boundary (serialization mismatches, duplicated DTOs).
- Tooling, debugging, and CI pipelines must accommodate multiple ecosystems.
- C# covers all required surfaces (API, web via Blazor Server, mobile via .NET MAUI) without compromise.

---

## Consequences

1. **Module isolation is the primary architectural constraint.** Every module has its own PostgreSQL schema, its own Contracts layer, and its own test suite. Architecture tests enforce these boundaries automatically.
2. **No network communication between modules.** All modules run in a single process. Inter-module events are dispatched in-memory. This eliminates distributed systems failure modes (timeouts, retries, circuit breakers) at the cost of single-process deployment.
3. **Single deployment.** One artifact is built, tested, and deployed. This simplifies CI/CD, monitoring, and rollback. Horizontal scaling is handled at the process level (multiple instances behind a load balancer), not at the module level.
4. **Future extraction is possible.** Because modules communicate only through contracts and events, and each owns its own database schema, extracting a module into a standalone service remains feasible if operational needs change. This decision does not prevent future evolution toward microservices.
5. **AI agents operate within bounded contexts.** An AI agent working on a module only needs that module's code, its Contracts dependencies, and the module documentation (MODULE.md, INVARIANTS.md, SCENARIOS.md). The architecture limits the required context window by design.
