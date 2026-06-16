namespace Liakont.Modules.FacturX.Infrastructure.Pdf;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Projette un <see cref="PivotDocumentDto"/> vers le <see cref="FacturXReadableModel"/> rendu en PDF
/// (F16 §5). Déterministe du pivot seul (ADR-0023 INV-FX-4) : aucune dépendance à un autre module,
/// aucune <c>PaCapabilities</c>. RECOPIE les valeurs qualitatives (taux BT-152, catégorie BT-151,
/// VATEX BT-121) du pivot ; n'invente aucune valeur fiscale (CLAUDE.md n°2). La ventilation de TVA
/// affichée agrège les montants PORTÉS par le pivot (per-ligne) par groupe (catégorie, taux, VATEX) —
/// même groupage que le sérialiseur CII (FX03) et que <c>SendArchiveComposer</c>, montants
/// <see cref="decimal"/> (n°1). Les totaux et le net à payer (BT-115 = BT-112 − BT-113) sont recopiés
/// du pivot. Appelée APRÈS la sérialisation CII (qui bloque sur un pivot non conforme), donc les lignes
/// portent une ventilation de TVA exploitable.
/// </summary>
internal static class PivotReadableProjection
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Projette le pivot vers le modèle lisible.</summary>
    public static FacturXReadableModel Project(PivotDocumentDto pivot)
    {
        ArgumentNullException.ThrowIfNull(pivot);

        var lines = pivot.Lines
            .Select(line => new FacturXReadableLine(
                line.Description,
                line.Quantity,
                line.UnitPriceNet,
                line.NetAmount,
                VatLabel(PrimaryTax(line))))
            .ToList();

        var prepaid = pivot.PrepaidAmount;
        var due = PivotRounding.RoundAmount(pivot.Totals.TotalGross - (prepaid ?? 0m));

        return new FacturXReadableModel(
            DocumentNumber: pivot.Number,
            DocumentTypeLabel: DocumentTypeLabel(pivot),
            IssueDate: DateOnly.FromDateTime(pivot.IssueDate),
            DueDate: pivot.PaymentDueDate is { } dueDate ? DateOnly.FromDateTime(dueDate) : null,
            CurrencyCode: pivot.CurrencyCode,
            SellerName: pivot.Supplier.Name,
            SellerSiren: pivot.Supplier.Siren,
            SellerVatNumber: pivot.Supplier.VatNumber,
            BuyerName: pivot.Customer?.Name,
            Lines: lines,
            VatBreakdown: BuildVatBreakdown(pivot),
            TotalNet: pivot.Totals.TotalNet,
            TotalTax: pivot.Totals.TotalTax,
            TotalGross: pivot.Totals.TotalGross,
            Prepaid: prepaid,
            DuePayable: due);
    }

    // Ventilation de TVA affichée : groupage par (catégorie, taux, VATEX) — clé identique au sérialiseur
    // CII (BG-23). Base = Σ nets du groupe ; TVA = Σ des montants de TVA PORTÉS par le pivot (per-ligne),
    // recopiés (jamais re-dérivés ici : c'est le sérialiseur qui réconcilie BR-CO-14). Ordre d'apparition.
    private static List<FacturXReadableVatLine> BuildVatBreakdown(PivotDocumentDto pivot)
    {
        var groups = new List<FacturXReadableVatLine>();
        var index = new Dictionary<(VatCategory?, decimal?, string?), int>();

        foreach (var line in pivot.Lines)
        {
            var tax = PrimaryTax(line);
            var key = (tax?.CategoryCode, tax?.Rate, tax?.VatexCode);
            var taxAmount = tax?.TaxAmount ?? 0m;
            if (index.TryGetValue(key, out var position))
            {
                var existing = groups[position];
                groups[position] = existing with
                {
                    TaxableBase = existing.TaxableBase + line.NetAmount,
                    TaxAmount = existing.TaxAmount + taxAmount,
                };
            }
            else
            {
                index[key] = groups.Count;
                groups.Add(new FacturXReadableVatLine(VatLabel(tax), line.NetAmount, taxAmount));
            }
        }

        return groups
            .Select(g => g with
            {
                TaxableBase = PivotRounding.RoundAmount(g.TaxableBase),
                TaxAmount = PivotRounding.RoundAmount(g.TaxAmount),
            })
            .ToList();
    }

    // Taux primaire d'une ligne (nominal : une ventilation par ligne, BG-30). Le sérialiseur CII bloque
    // en amont si ce n'est pas le cas ; ici on reste défensif (rendu lisible best-effort).
    private static PivotLineTaxDto? PrimaryTax(PivotLineDto line) =>
        line.Taxes.Count > 0 ? line.Taxes[0] : null;

    // Libellé du taux RECOPIÉ du pivot, complété de la catégorie (BT-151) et du code VATEX (BT-121) quand
    // ils sont portés — valeurs du pivot, jamais inventées (CLAUDE.md n°2). Présentation uniquement.
    private static string VatLabel(PivotLineTaxDto? tax)
    {
        if (tax is null)
        {
            return "Taux non précisé";
        }

        var ratePart = tax.Rate.HasValue
            ? tax.Rate.Value.ToString("0.##", Fr) + " %"
            : "Taux non précisé";

        var category = tax.CategoryCode?.ToString();
        if (string.IsNullOrEmpty(category))
        {
            return ratePart;
        }

        return string.IsNullOrWhiteSpace(tax.VatexCode)
            ? $"{ratePart} ({category})"
            : $"{ratePart} ({category}, {tax.VatexCode})";
    }

    // « Avoir » si le pivot porte des références d'avoir (BT-25), sinon « Facture » — même règle que
    // SendArchiveComposer. La classification fine (380/381) relève du module Validation, pas d'ici.
    private static string DocumentTypeLabel(PivotDocumentDto pivot) =>
        pivot.CreditNoteRefs.Count > 0 ? "Avoir" : "Facture";
}
