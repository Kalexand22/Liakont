namespace Liakont.Agent.Adapters.DemoErpB.Source;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>InvoiceItem</c> (DemoErpB). Montants en <c>float</c> legacy.
/// Le code régime TVA (<c>VatRegime</c>) est transporté BRUT (R3) — jamais interprété par l'agent.
/// </summary>
internal sealed class DemoErpBItem
{
    /// <summary>Numéro de ligne dans la source (<c>LineNumber</c>).</summary>
    public string? LineNumber { get; set; }

    /// <summary>Libellé de la ligne (EN 16931 BT-153).</summary>
    public string? Label { get; set; }

    /// <summary>Quantité (EN 16931 BT-129), flottant legacy.</summary>
    public double Qty { get; set; }

    /// <summary>Prix unitaire HT (EN 16931 BT-146), <c>null</c> si absent.</summary>
    public double? UnitPrice { get; set; }

    /// <summary>Montant HT de la ligne (EN 16931 BT-131), flottant legacy.</summary>
    public double NetAmount { get; set; }

    /// <summary>Montant de TVA de la ligne, flottant legacy.</summary>
    public double VatAmount { get; set; }

    /// <summary>Taux de TVA en pourcentage (EN 16931 BT-152), <c>null</c> si absent.</summary>
    public double? VatRate { get; set; }

    /// <summary>Code régime TVA BRUT (R3) — jamais interprété par l'agent (CLAUDE.md n°2).</summary>
    public string? VatRegime { get; set; }
}
