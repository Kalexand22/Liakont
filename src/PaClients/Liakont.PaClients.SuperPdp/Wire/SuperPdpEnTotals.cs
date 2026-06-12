namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Totaux monétaires du document (EN 16931 BG-22, schéma <c>totals</c> de l'OpenAPI — requis :
/// <c>sum_invoice_lines_amount</c>, <c>total_without_vat</c>, <c>total_with_vat</c>,
/// <c>amount_due_for_payment</c>). Montants RECOPIÉS du pivot en <see cref="decimal"/> (CLAUDE.md n°1) —
/// le plug-in ne calcule rien, la validation EN 16931 du converter vérifie la cohérence (BR-CO-*).
/// </summary>
internal sealed record SuperPdpEnTotals
{
    /// <summary>Somme des montants nets de lignes (EN 16931 BT-106).</summary>
    public required decimal SumInvoiceLinesAmount { get; init; }

    /// <summary>Total HT (EN 16931 BT-109).</summary>
    public required decimal TotalWithoutVat { get; init; }

    /// <summary>Total de TVA (EN 16931 BT-110), avec sa devise.</summary>
    public SuperPdpEnAmount? TotalVatAmount { get; init; }

    /// <summary>Total TTC (EN 16931 BT-112).</summary>
    public required decimal TotalWithVat { get; init; }

    /// <summary>Montant déjà payé / acompte (EN 16931 BT-113), omis si la source n'en porte pas.</summary>
    public decimal? PaidAmount { get; init; }

    /// <summary>Montant à payer (EN 16931 BT-115) — identité normative BR-CO-16 : BT-115 = BT-112 − BT-113.</summary>
    public required decimal AmountDueForPayment { get; init; }
}
