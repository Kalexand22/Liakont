namespace Liakont.Agent.Adapters.DemoErpA.Source;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>lignes_facture</c> (DemoErpA). Montants en <c>decimal</c>.
/// Le code régime TVA est transporté BRUT (R3) — jamais interprété ni mappé par l'agent.
/// </summary>
internal sealed class DemoErpALine
{
    /// <summary>Référence de la ligne dans la source (<c>no_ligne</c>).</summary>
    public string? NoLigne { get; set; }

    /// <summary>Libellé de la ligne (EN 16931 BT-153).</summary>
    public string? Designation { get; set; }

    /// <summary>Quantité (EN 16931 BT-129).</summary>
    public decimal Quantite { get; set; }

    /// <summary>Prix unitaire HT (EN 16931 BT-146), <c>null</c> si absent.</summary>
    public decimal? PrixUnitaire { get; set; }

    /// <summary>Montant HT de la ligne (EN 16931 BT-131).</summary>
    public decimal MontantHt { get; set; }

    /// <summary>Montant de TVA de la ligne.</summary>
    public decimal MontantTva { get; set; }

    /// <summary>Taux de TVA en pourcentage (EN 16931 BT-152), <c>null</c> si absent.</summary>
    public decimal? TauxTva { get; set; }

    /// <summary>Code régime TVA BRUT (R3) — jamais interprété par l'agent (CLAUDE.md n°2).</summary>
    public string? CodeRegime { get; set; }
}
