namespace Liakont.Agent.Adapters.EncheresV6;

/// <summary>
/// Enregistrement BRUT d'un frais vendeur (commission vendeur, « bordereau vendeur » / BV) extrait de
/// la source EncheresV6 (lignes <c>type_ligne = "5"</c>, F01-F02 §4.3.1, B2C-06/07). C'est une DONNÉE
/// DE CALCUL de la marge e-reporting B2C (F03 §2.4 : la marge agrège les frais acheteur ET vendeur,
/// formule sourcée par B2C-05) — JAMAIS une ligne facturée à l'acheteur (l'extraction document l'ignore,
/// art. 297 E, B2C-08).
/// <para>
/// EXTRACTION PURE (CLAUDE.md n°6, R3) : l'adaptateur ne fait AUCUNE logique fiscale ni calcul de marge
/// (cela vit sur la plateforme, B2C-09b). Le seul traitement est la conversion OBLIGATOIRE du montant
/// legacy (flottant Pervasive) en <see cref="decimal"/> au centime arrondi half-up à la frontière
/// (CLAUDE.md n°1, ADR-0004 D3-7) — jamais de <c>float</c>/<c>double</c> sur un montant exposé. Le code
/// régime est transporté BRUT (le mapping F03 est plateforme). Aucune TVA n'est modélisée : sous le
/// régime de la marge aucune TVA distincte n'apparaît (art. 297 E) et le frais vendeur n'est qu'un terme
/// HT de la formule.
/// </para>
/// <para>
/// Rattachement au lot par le <see cref="NoBa"/> du bordereau (grain bordereau — le <c>no_lot</c> présumé
/// n'existe pas dans le schéma EncheresV6, F01-F02 §4.3.1). B2C-08 portera ce frais dans le pivot de façon
/// hash-neutre, au grain lot ; B2C-09b l'utilisera pour le calcul de marge.
/// </para>
/// </summary>
public sealed class EncheresV6SellerFee
{
    /// <summary>Crée un enregistrement de frais vendeur brut.</summary>
    /// <param name="noBa">Identifiant du bordereau de rattachement (<c>no_ba</c>) — clé au grain lot.</param>
    /// <param name="netAmount">Montant HT du frais vendeur, en <c>decimal</c> arrondi au centime (half-up) — terme de la marge.</param>
    /// <param name="sourceRegimeCode">Code régime TVA source de la ligne, BRUT (jamais interprété — R3). <c>null</c> si absent.</param>
    /// <param name="sourceLineRef">Référence de la ligne dans la source (<c>no_ligne</c>), pour la traçabilité. <c>null</c> si absent.</param>
    /// <param name="description">Libellé de la ligne source (<c>designation</c>), informatif. <c>null</c> si absent.</param>
    public EncheresV6SellerFee(
        string noBa,
        decimal netAmount,
        string? sourceRegimeCode,
        string? sourceLineRef,
        string? description)
    {
        NoBa = noBa;
        NetAmount = netAmount;
        SourceRegimeCode = sourceRegimeCode;
        SourceLineRef = sourceLineRef;
        Description = description;
    }

    /// <summary>Identifiant du bordereau de rattachement (<c>no_ba</c>) — clé du frais vendeur au grain lot.</summary>
    public string NoBa { get; }

    /// <summary>Montant HT du frais vendeur, en <c>decimal</c> arrondi au centime (half-up) — terme de la formule de marge.</summary>
    public decimal NetAmount { get; }

    /// <summary>Code régime TVA source de la ligne, BRUT (jamais mappé/interprété — R3, CLAUDE.md n°2). <c>null</c> si absent.</summary>
    public string? SourceRegimeCode { get; }

    /// <summary>Référence de la ligne dans le système source (<c>no_ligne</c>), pour la traçabilité. <c>null</c> si absent.</summary>
    public string? SourceLineRef { get; }

    /// <summary>Libellé de la ligne source (<c>designation</c>), informatif. <c>null</c> si absent.</summary>
    public string? Description { get; }
}
