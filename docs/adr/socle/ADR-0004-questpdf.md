# ADR-0004: QuestPDF for Server-Side PDF Generation

**Date:** 2026-03-20

**Status:** Accepted

---

## Context

StratumDataGrid provides built-in CSV export (W04/U04). The next step (W10) adds PDF export to allow users to generate printable, paginated reports directly from data grids. PDF generation requires:

- Page layout: headers, data rows, page numbers, export date
- Automatic landscape orientation for wide grids
- Alternating row colors for readability
- Server-side generation (no browser dependency)

Building a PDF generator from scratch is disproportionate. A library is the pragmatic choice, provided:

1. It is MIT-licensed (no copyleft risk).
2. It is encapsulated inside `Stratum.Common.UI` (never exposed to pages or modules).
3. It has no heavy native dependencies that complicate deployment.

Per `docs/architecture/allowed-dependencies.md`, any package not in the approved list requires an ADR and human approval.

---

## Decision

### Adopt QuestPDF as internal PDF engine

QuestPDF is adopted as the PDF generation library for server-side export. It is referenced **only** in `Stratum.Common.UI.csproj` and consumed **only** by the internal `PdfExportHelper` class. No QuestPDF types appear in any public API.

### Comparison: QuestPDF vs alternatives

| Criterion         | QuestPDF               | iTextSharp/iText7     | PdfSharpCore          |
|-------------------|------------------------|-----------------------|-----------------------|
| **License**       | MIT (Community)        | AGPL / Commercial     | MIT                   |
| **API ergonomics**| Fluent, declarative    | Imperative, verbose   | Imperative, low-level |
| **Layout engine** | Automatic pagination   | Manual page breaks    | Manual positioning    |
| **Table support** | Built-in with headers  | Manual cell placement | No built-in tables    |
| **.NET support**  | .NET 6+                | .NET 6+               | .NET 6+               |
| **Dependencies**  | Pure managed code      | BouncyCastle (heavy)  | Pure managed code     |
| **Maturity**      | 10M+ NuGet downloads   | Industry standard     | Moderate              |

**QuestPDF wins** on API ergonomics (fluent layout, automatic pagination), MIT license without AGPL risk, and zero heavy native dependencies. PdfSharpCore lacks built-in table support. iText7's AGPL license is incompatible without a commercial license.

### Encapsulation rules

QuestPDF follows the same encapsulation discipline as Radzen.Blazor (ADR-0003):

1. **R-PDF-1:** QuestPDF is referenced only in `Stratum.Common.UI.csproj`.
2. **R-PDF-2:** No QuestPDF types appear in any public parameter, return type, or event.
3. **R-PDF-3:** `PdfExportHelper` is `internal static` — invisible outside Common.UI.
4. **R-PDF-4:** Architecture tests (`check-deps`) enforce that no other project references QuestPDF.

### License configuration

QuestPDF Community license is free for companies with annual revenue under $1M USD. The license type is configured once at startup in `AddCommonUI()`:

```csharp
QuestPDF.Settings.License = LicenseType.Community;
```

---

## Consequences

1. `QuestPDF` is added to `Directory.Packages.props` (centrally managed version).
2. `Stratum.Common.UI.csproj` gains a `<PackageReference Include="QuestPDF" />`.
3. `PdfExportHelper` (internal) generates PDF from column/row data.
4. `StratumDataGrid` offers built-in PDF export via `ExportFormat.Pdf`.
5. If QuestPDF ever needs replacement, only `PdfExportHelper` changes — no impact on pages.
6. Architecture tests enforce the isolation (same `check-deps` mechanism as Radzen).

---

## References

- `docs/architecture/ui-architecture-plan.md` — UI architecture plan
- `docs/adr/ADR-0003-radzen-ui.md` — Radzen encapsulation precedent
- `docs/architecture/allowed-dependencies.md` — Dependency governance rules
