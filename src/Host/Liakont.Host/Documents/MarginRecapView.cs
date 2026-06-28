namespace Liakont.Host.Documents;

/// <summary>
/// Récapitulatif de marge AFFICHABLE d'un document au régime de la marge (art. 297 E), onglet « Contenu » du détail.
/// Aide à la déclaration de TVA : la TVA sur marge n'apparaît PAS sur la facture (297 E) mais est due par l'opérateur
/// dans sa déclaration (CA3) — ce bloc la rend lisible. PROJECTION pure du
/// <see cref="Liakont.Modules.Pipeline.Contracts.DocumentMarginRecapDto"/> (calcul fiscal dans le module Pipeline) :
/// aucune règle métier ni calcul ici. <c>null</c> sur le modèle quand le document n'est pas au régime de la marge.
/// </summary>
public sealed record MarginRecapView
{
    /// <summary>Commission acheteur TTC (Σ lignes d'honoraire acheteur).</summary>
    public required decimal BuyerFeesTtc { get; init; }

    /// <summary>Commission vendeur TTC (décompte vendeur BV).</summary>
    public required decimal SellerFeesTtc { get; init; }

    /// <summary>Marge TTC = commission acheteur + vendeur.</summary>
    public required decimal MarginTtc { get; init; }

    /// <summary>Base de marge HT (marge TTC ramenée HT au taux).</summary>
    public required decimal BaseHt { get; init; }

    /// <summary>TVA sur marge à déclarer par l'opérateur.</summary>
    public required decimal Tva { get; init; }

    /// <summary>Taux de TVA de la vente (pourcentage).</summary>
    public required decimal RatePercent { get; init; }
}
