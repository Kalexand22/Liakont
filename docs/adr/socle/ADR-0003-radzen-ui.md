# ADR-0003: Radzen.Blazor as Internal UI Engine for Complex Components

**Date:** 2026-03-19

**Status:** Accepted

---

## Context

Stratum possesses 21 custom Blazor components in `Stratum.Common.UI`, covering accessibility (WCAG AA), keyboard navigation, and a design token system (`tokens.css`). Simple components (StatusBadge, Toast, FormField, PageHeader, SectionCard, ActionBar, PermissionGate, ConnectionStatus, shortcuts) are mature and fit for purpose.

However, the current `DataTable<TItem>` component lacks critical ERP features:

- No virtual scrolling (performance degrades beyond ~200 rows)
- No inline editing (required for quote/order line entry)
- No column resize or reorder
- No row grouping
- No data export (CSV/Excel)
- No frozen columns

Building these features from scratch would require 2000+ lines of complex code with significant JavaScript interop edge cases. Additional complex components are also needed: a rich date picker (calendar with min/max, keyboard navigation), a tree view (chart of accounts, category hierarchies), context menus, split buttons, and more.

A third-party Blazor component library is the pragmatic choice for these complex components, **provided** the dependency is fully encapsulated behind the existing `Stratum.Common.UI` abstraction layer.

Per `docs/architecture/allowed-dependencies.md`, any package not in the approved list requires an ADR and human approval.

---

## Decision

### Adopt Radzen.Blazor as internal engine for complex UI components

Radzen.Blazor (MIT-licensed community package) is adopted as the rendering engine for complex components. It is referenced **only** in `Stratum.Common.UI.csproj` and **never** exposed to business pages or module code.

### Comparison: Radzen vs MudBlazor

| Criterion | Radzen.Blazor | MudBlazor |
|-----------|---------------|-----------|
| **DataGrid (ERP use)** | Most complete in Blazor ecosystem: inline edit, virtual scroll, grouping, frozen columns, column resize/reorder, export, conditional formatting | MudDataGrid improving but less mature for ERP (limited grouping, basic export) |
| **ERP-critical components** | Scheduler, TreeView, SplitButton, advanced Menu, Charts, FileUpload | TreeView, Menu, Charts (via ApexCharts addon), basic FileUpload |
| **License** | MIT (community Blazor components). Professional tier optional (support + premium components) | 100% MIT, community-driven |
| **Theming / Customization** | CSS variables + ThemeService, visually neutral | Material Design opinionated, rich theme system but imposed aesthetic |
| **CSS footprint** | Lighter (no bundled CSS framework) | Bundles its own CSS system (partial Bootstrap incompatibility) |
| **Bootstrap compatibility** | Good — uses standard classes, no conflicts | Potential conflicts — injects its own CSS system |
| **Accessibility** | Adequate (basic ARIA), requires manual supplementation | Good on common components, uneven on complex ones |
| **C# API style** | Close to native HTML, explicit parameters | More abstract, "fluent" but sometimes opaque |
| **Maturity** | Since 2019, stable, backed by Radzen Ltd | Since 2020, very active community |
| **Blazor Server perf** | Optimized (native virtualization) | Adequate, virtualization available |

### Why Radzen over MudBlazor

1. **DataGrid superiority.** The DataGrid is the #1 component in an ERP. RadzenDataGrid is the most feature-complete in the Blazor ecosystem for ERP use cases (inline editing, virtual scrolling, frozen columns, grouping, export). MudDataGrid is not at the same level for intensive ERP use.

2. **Bootstrap compatibility.** Stratum uses Bootstrap 5.3.3 for grid and utilities. Radzen integrates without CSS conflicts. MudBlazor imposes Material Design, which collides with Bootstrap.

3. **Visual neutrality.** Radzen is visually unopinionated and styles via CSS variables, allowing Stratum's design tokens to fully control appearance. MudBlazor's Material Design aesthetic creates a mismatch with existing Stratum components and complicates ERP branding.

4. **Lighter footprint.** Radzen does not force a complete CSS framework. Only what is used is loaded.

5. **MIT license.** The Radzen Blazor community components are MIT-licensed. The Professional tier is optional and not required for Phase 5 needs.

### What we do NOT do

- **No replacement** of existing Stratum components that work (Toast, StatusBadge, FormField, Lookup, etc.)
- **No direct dependency** from business code to Radzen
- **No Radzen default CSS** loaded — all styling via Stratum design tokens
- If Radzen disappoints on a specific component, it can be replaced without business code impact

---

## Encapsulation Rules

These rules govern how Radzen is integrated. They are enforced by architecture tests (see S06 in the work manifest).

### R1: Public vs Internal namespaces

```
Stratum.Common.UI.Components           → Public, consumed by pages
Stratum.Common.UI.Components.Internal  → Never used directly by pages
Stratum.Common.UI.Services             → Public (interfaces)
Stratum.Common.UI.Models               → Public (UI DTOs)
```

The `_Imports.razor` of Common.UI exports public namespaces only. `Internal` namespaces are not exported.

### R2: Parameters use Stratum types only

No public parameter of a Stratum component may expose a Radzen type.

```csharp
// CORRECT — Stratum type
[Parameter] public SortDirection SortDirection { get; set; }

// FORBIDDEN — Radzen type exposed
[Parameter] public Radzen.SortOrder SortOrder { get; set; }
```

Type mapping (Stratum → Radzen) happens **inside** the wrapper component, never in page code.

### R3: No `@using Radzen` in business pages

Business pages (`Host/Components/Pages/*`, `Module.Web/Pages/*`) must never contain `@using Radzen` or `<Radzen*` tags. Enforced by architecture test.

### R4: Events use simple types

Callbacks use simple types or Stratum records, never Radzen types:

```csharp
// CORRECT
public EventCallback<SortChangedArgs> OnSortChanged { get; set; }

// FORBIDDEN
public EventCallback<Radzen.LoadDataArgs> OnLoadData { get; set; }
```

### R5: Each wrapper is a testable facade

Each wrapper component:
1. Defines `[Parameter]` properties using Stratum types
2. Maps internally to the Radzen component
3. Converts Radzen events → Stratum events
4. Adds missing ARIA/accessibility attributes
5. Is covered by a bUnit test

### R6: CSS isolation per component

Each wrapper has its own `.razor.css` (scoped) or uses `tokens.css` classes. No global Radzen CSS is loaded in `App.razor`.

### R7: Single PackageReference

```xml
<!-- Stratum.Common.UI.csproj — ONLY project allowed -->
<PackageReference Include="Radzen.Blazor" />

<!-- ALL other .csproj — FORBIDDEN -->
```

---

## Consequences

1. **Radzen.Blazor is added to `Directory.Packages.props`** with a locked version. The `PackageReference` exists only in `Stratum.Common.UI.csproj`.

2. **Four architecture tests** are added to `Stratum.Tests.Architecture` to enforce rules R1, R3, R2/R4, and R7. These tests fail the build on violation.

3. **A CSS override file** (`radzen-overrides.css`) maps all `--rz-*` CSS variables to Stratum design tokens. No default Radzen theme CSS is loaded.

4. **Business pages are unaffected.** They consume `<StratumDataGrid>`, `<StratumDatePicker>`, etc. — never Radzen components directly. A future switch from Radzen to another library would not touch business pages.

5. **The existing Lookup, Toast, FormField, and other mature components are preserved.** Radzen is adopted for components where custom implementation would be disproportionately expensive (DataGrid, DatePicker, TreeView, etc.).

6. **This ADR must be accepted before any implementation** of Radzen wrapper components (items S04+ in the work manifest).

---

## References

- [UI Architecture Plan](../architecture/ui-architecture-plan.md) — full component catalog, CSS strategy, implementation phases
- [ADR-0001: Architecture Base](ADR-0001-architecture-base.md) — modular monolith foundation
- [ADR-0002: Frontend Strategy](ADR-0002-frontend-strategy.md) — Blazor Server render mode, no logic in components
- [Allowed Dependencies](../architecture/allowed-dependencies.md) — dependency governance rules
