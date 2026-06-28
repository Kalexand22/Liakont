namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Récapitulatif de marge d'UN document au régime de la marge (art. 297 A/E), pour l'aide à la déclaration de TVA
/// du détail document : la TVA sur marge n'apparaît PAS sur la facture (297 E) mais est due par l'opérateur dans sa
/// déclaration (CA3) — ce récap la rend visible. Dérivé du pivot transmis/rejoué + mapping F03 (<c>Part.Frais</c>) :
/// commission acheteur + vendeur (F03 §2.4/§270), ramenées HT (F03 §2.5). Tous montants en <see cref="decimal"/>
/// (CLAUDE.md n°1). <c>null</c> côté query si le document n'est pas au régime de la marge ou si la marge est bloquée.
/// </summary>
public sealed record DocumentMarginRecapDto
{
    /// <summary>Commission ACHETEUR TTC (Σ lignes rôle BuyerFee).</summary>
    public required decimal BuyerFeesTtc { get; init; }

    /// <summary>Commission VENDEUR TTC (Σ SellerFees, décompte BV).</summary>
    public required decimal SellerFeesTtc { get; init; }

    /// <summary>Marge TTC = commission acheteur + vendeur (F03 §2.4 / BOI-TVA-SECT-90-50 §270).</summary>
    public required decimal MarginTtc { get; init; }

    /// <summary>Base de marge HT = marge TTC ramenée HT au taux (F03 §2.5).</summary>
    public required decimal BaseHt { get; init; }

    /// <summary>TVA sur marge = marge TTC − base HT (à déclarer par l'opérateur).</summary>
    public required decimal Tva { get; init; }

    /// <summary>Taux de TVA unique de la vente (mapping F03 de l'honoraire).</summary>
    public required decimal RatePercent { get; init; }
}
