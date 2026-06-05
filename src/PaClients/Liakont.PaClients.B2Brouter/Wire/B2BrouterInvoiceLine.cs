namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Une ligne de facture B2Brouter. Les montants viennent du pivot (déjà calculés par la source —
/// le plug-in ne calcule rien, F01-F02 §3.7), en <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
internal sealed record B2BrouterInvoiceLine
{
    /// <summary>Libellé de la ligne (EN 16931 BT-153).</summary>
    public required string Description { get; init; }

    /// <summary>Quantité (EN 16931 BT-129).</summary>
    public required decimal Quantity { get; init; }

    /// <summary>Prix / montant HT de la ligne (EN 16931 BT-131).</summary>
    public required decimal Price { get; init; }

    /// <summary>Ventilation de TVA de la ligne (catégorie + taux + VATEX), ou <c>null</c> si non ventilée.</summary>
    public B2BrouterTax? Tax { get; init; }
}
