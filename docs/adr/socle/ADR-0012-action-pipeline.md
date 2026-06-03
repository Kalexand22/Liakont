# ADR-0012: Action Pipeline Architecture

**Date:** 2026-03-31

**Status:** Accepted

---

## Context

Stratum's modular monolith currently handles commands via MediatR handlers with inline validation logic in domain entity factory methods. As the ERP grows (Sales, Finance, Tax, etc.), each module will need:

1. **Declarative validation** — reusable, composable validation rules separated from entity internals.
2. **Field change reactions** — recalculating dependent fields when a value changes (e.g., changing a customer recalculates delivery address and payment terms).
3. **Dynamic UI attributes** — controlling hidden/readonly/required/domain-filter per field based on entity state.
4. **Action chaining** — composing validate-then-compute-then-execute sequences with conditional steps and stop-on-error.
5. **Cross-module hooks** — allowing one module to validate or enrich another module's operations without violating module isolation.

A benchmark of 11 ERP systems (Axelor, Odoo, ERPNext, D365, NetSuite, etc.) identified recurring patterns: Axelor's declarative XML actions, D365's staged plugin pipeline, ERPNext's cross-module hooks, Odoo's `@api.onchange`/`@api.depends` decorators. Stratum needs an equivalent system adapted to its C#/MediatR/Blazor Server stack.

**Constraints:**
- Must integrate with MediatR (not replace it). MediatR dispatches commands; the action pipeline orchestrates business logic inside the handler.
- Must respect module isolation (ADR-0001). Cross-module hooks cannot write to another module's data.
- Must work with Blazor Server (no client-side JavaScript engine). All logic stays server-side.
- Must be testable without a browser or database (unit-testable rules and validators).

---

## Decision

### Adopt a staged Action Pipeline with 6 composable pillars

The action pipeline executes **inside** MediatR command handlers, providing a structured way to compose business logic. It does **not** replace MediatR behaviors (which handle cross-cutting concerns like tenant propagation and collaboration events).

### Pillar 1: Action Pipeline (staged execution)

A 4-stage synchronous pipeline executes within the command handler's unit of work:

| Stage | Number | Purpose | Example |
|-------|--------|---------|---------|
| Pre-Validation | 10 | Validate business rules, block if invalid | Check required fields, check state transitions |
| Pre-Operation | 20 | Enrich, compute, react to changes | Recalculate totals, set default values |
| Main Operation | 30 | Core domain mutation | Create/update entity, persist |
| Post-Operation | 40 | Side effects within same transaction | Write outbox event, update audit |

**Key interfaces:**
- `IActionPipeline` — orchestrates step execution in stage/order sequence.
- `IActionStep<TContext>` — a single step with `Stage`, `Order`, and `ExecuteAsync()`.
- `ActionContext<TEntity>` — carries entity, actor, changed fields, cancellation.

**Location:** `Common/Abstractions/Actions/` (interfaces), `Common/Infrastructure/Actions/` (implementation).

### Pillar 2: Declarative Validation

Two levels, both evaluated in Pre-Validation (Stage 10):

- `IEntityValidator<T>` — validates the whole entity, returns `ValidationResult` with findings.
- `IFieldValidator<T>` — validates a single field with a targeted error message.

`ValidationResult` carries severity (Error/Warning/Info), optional field name, message, and invariant code (e.g., `INV-COMP-001`).

**Location:** Interfaces in `Common/Abstractions/Validation/`. Implementations in `{Module}/Application/Validators/`. Domain entities keep their inline validations for hard invariants in factory methods.

### Pillar 3: Field Change Reactions

Handlers decorated with `[OnChange("FieldName")]` execute in Pre-Operation (Stage 20) when the specified field has changed:

```csharp
public class SaleOrderFieldChanges : IFieldChangeHandler<SaleOrder>
{
    [OnChange(nameof(SaleOrder.ClientPartner))]
    public FieldChangeResult OnClientChanged(FieldChangeContext<SaleOrder> ctx)
    {
        return ctx.SetFields(new
        {
            DeliveryAddress = ctx.Entity.ClientPartner?.DefaultDeliveryAddress,
            PaymentTerms = ctx.Entity.ClientPartner?.DefaultPaymentTerms
        });
    }
}
```

`FieldChangeContext<T>` provides: entity before/after, changed field name, previous value. `FieldChangeResult` returns values to set and UI attributes to modify.

**Location:** `Common/Abstractions/FieldChange/` (interfaces), `{Module}/Application/FieldChangeHandlers/` (implementations).

### Pillar 4: Dynamic UI Rules

`IUiRuleProvider<TDto>` declares visibility, editability, and required-ness rules using C# lambdas:

```csharp
public class SaleOrderUiRules : IUiRuleProvider<SaleOrderDto>
{
    public IEnumerable<UiRule<SaleOrderDto>> GetRules() => new[]
    {
        Rule.For(x => x.Discount).HiddenWhen(x => x.Status != "Draft"),
        Rule.For(x => x.DeliveryDate).RequiredWhen(x => x.Status == "Confirmed"),
    };
}
```

The `UiRuleEngine` evaluates rules and produces a `UiAttributeSet` (dictionary of field -> hidden/readonly/required/domain). Blazor components (`FormField`, `DataGrid`) consume `UiAttributeSet` natively.

**Location:** Interfaces in `Common/Abstractions/UiRules/`. Engine in `Common/Infrastructure/UiRules/`. Implementations in `{Module}/Application/UiRules/`.

### Pillar 5: Action Chains

Fluent API to compose multi-step sequences with conditional execution and stop-on-error:

```csharp
public class SaleOrderConfirmChain : IActionChain<SaleOrder>
{
    public void Configure(IActionChainBuilder<SaleOrder> builder)
    {
        builder
            .Validate<SaleOrderConfirmValidator>()
            .Execute<ComputeFinalPrices>()
            .Execute<GenerateDeliveryOrder>(when: ctx => ctx.Config.AutoDelivery);
    }
}
```

**Location:** `Common/Abstractions/Actions/Chains/`, `{Module}/Application/Chains/`.

### Pillar 6: Cross-Module Hooks

A module can react to another module's pipeline actions via `IActionHook`:

```csharp
[Hook("sale.sale-order.confirmed", Stage.PreValidation)]
public async Task OnSaleOrderConfirmed(ActionContext<SaleOrderDto> ctx)
{
    // Read-only: validate stock availability
}
```

**Critical rule:** Hooks in Pre-Validation and Pre-Operation stages are **read-only**. They can validate, enrich the response, or block the operation, but they **cannot write to another module's data**. Cross-module writes happen exclusively through IntegrationEvents via the existing outbox pattern (Post-Operation). This preserves module isolation (ADR-0001).

Hooks are discovered at startup via DI assembly scanning.

**Location:** `Common/Abstractions/Actions/Hooks/`, `{Module}/Application/Hooks/`.

### Action Registry

All pipeline actions, validators, field change handlers, UI rules, chains, and hooks are indexed in `docs/actions/action-registry.yaml`, following the same pattern as `docs/events/event-registry.yaml`.

---

## Rejected Alternatives

### Replace MediatR with a custom pipeline

Rejected because MediatR is proven infrastructure for command dispatch, and its behaviors handle cross-cutting concerns (tenant, collaboration) well. The action pipeline is complementary: MediatR dispatches, the pipeline orchestrates business logic inside the handler. Replacing MediatR would force rewriting all existing handlers and behaviors for no architectural gain.

### String-based DSL for UI rules (e.g., JEXL, Groovy)

Rejected for v1 because:
- C# lambdas are type-safe, refactorable, and unit-testable without a runtime interpreter.
- A DSL requires a parser, an evaluation engine, and debugging tools that don't exist.
- The team (human + AI agents) is proficient in C#. Adding a second language increases context switching.
- If a no-code editing need emerges (e.g., for business analysts), a string DSL can be added in v2 without breaking the lambda-based foundation.

### Async pipeline with eventual consistency

Rejected because:
- The pipeline runs inside a unit of work. Validation and field computation must be synchronous to guarantee consistency before commit.
- Async side effects already have a proven path: IntegrationEvents via outbox.
- Adding async steps inside the pipeline would require saga/compensation logic that is disproportionate to current needs.

### Cross-module hooks with write access (same transaction)

Rejected because ADR-0001 forbids cross-module data writes. Allowing a Sales hook to write Supplychain data in the same transaction would create hidden coupling, shared transaction scope, and cascading failures. The outbox/event pattern already handles this safely with eventual consistency.

---

## Consequences

1. MediatR command handlers gain a structured way to compose business logic instead of ad-hoc inline code. Handlers become thinner orchestrators.
2. Validators are reusable across commands and testable in isolation (no database, no UI).
3. Field change reactions are explicit and discoverable, not buried in component code-behind.
4. UI rules are evaluated server-side and pushed to Blazor components as data, keeping components generic and logic-free.
5. Action chains enable complex workflows (e.g., sale order confirmation) as composable, testable units.
6. Cross-module hooks provide a controlled extension point that respects module boundaries.
7. The action registry provides discoverability for human developers and AI agents.
8. Domain entities retain their inline invariant checks (factory methods). The pipeline validators handle composite and cross-entity business rules that don't belong in a single entity.
9. No new external dependencies are required. The pipeline is pure C# infrastructure.

---

## References

- ADR-0001: Architecture Base (modular monolith, module isolation)
- `docs/architecture/module-rules.md` (cross-module communication rules)
- `docs/architecture/event-conventions.md` (integration event patterns)
- `tasks/plan-actions-events.md` (implementation plan, 4 phases)
- `ANALYSE_ACTIONS_ERP.md` (benchmark of 11 ERP action systems)
