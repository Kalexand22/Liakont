namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Frais vendeur (commission vendeur, « bordereau vendeur » / BV) porté dans le pivot comme DONNÉE DE
/// CALCUL de la marge e-reporting B2C (B2C-08, F03 §2.4). DTO PUR : aucun calcul, aucune règle fiscale ;
/// il transporte le terme HT extrait par la source (côté agent : <c>EncheresV6SellerFee</c>, B2C-07) au
/// grain lot, pour que la plateforme calcule la marge (B2C-09b).
/// <para>
/// Ce n'est JAMAIS une ligne facturée à l'acheteur ni une ventilation de TVA : sous le régime de la marge
/// (CGI art. 297 E) aucune TVA distincte n'apparaît, et le frais vendeur ne gonfle pas la base imposable
/// du document 10.3 (B2C-08, règle n°2). D'où l'ABSENCE de tout champ de taxe ici (pas de <c>TaxAmount</c>,
/// <c>Rate</c>, catégorie ni VATEX). Il est porté hors de <see cref="PivotDocumentDto.Lines"/> et
/// n'impacte pas <see cref="PivotTotalsDto"/>.
/// </para>
/// Montant en <see cref="decimal"/> (CLAUDE.md n°1). Le code régime est transporté BRUT (le mapping F03
/// est plateforme — jamais interprété ici, CLAUDE.md n°2).
/// </summary>
public sealed class PivotSellerFeeDto
{
    /// <summary>Crée un frais vendeur pivot (donnée de calcul de marge, grain lot).</summary>
    /// <param name="lotReference">
    /// Identifiant du lot de rattachement (côté EncheresV6 : le bordereau <c>no_ba</c>) — clé au grain lot.
    /// </param>
    /// <param name="netAmount">Montant HT du frais vendeur, en <c>decimal</c> (arrondi half-up par la source) — terme de la marge.</param>
    /// <param name="sourceRegimeCode">Code régime TVA source de la ligne, BRUT (jamais interprété — CLAUDE.md n°2). <c>null</c> si absent.</param>
    /// <param name="sourceLineRef">Référence de la ligne dans le système source, pour la traçabilité. <c>null</c> si absent.</param>
    /// <param name="description">Libellé de la ligne source, informatif. <c>null</c> si absent.</param>
    public PivotSellerFeeDto(
        string lotReference,
        decimal netAmount,
        string? sourceRegimeCode = null,
        string? sourceLineRef = null,
        string? description = null)
    {
        LotReference = lotReference;
        NetAmount = netAmount;
        SourceRegimeCode = sourceRegimeCode;
        SourceLineRef = sourceLineRef;
        Description = description;
    }

    /// <summary>Identifiant du lot de rattachement (grain lot — côté EncheresV6 : <c>no_ba</c>).</summary>
    public string LotReference { get; }

    /// <summary>Montant HT du frais vendeur, en <c>decimal</c> — terme de la formule de marge (B2C-09b).</summary>
    public decimal NetAmount { get; }

    /// <summary>Code régime TVA source de la ligne, BRUT (jamais mappé/interprété — CLAUDE.md n°2). <c>null</c> si absent.</summary>
    public string? SourceRegimeCode { get; }

    /// <summary>Référence de la ligne dans le système source, pour la traçabilité. <c>null</c> si absent.</summary>
    public string? SourceLineRef { get; }

    /// <summary>Libellé de la ligne source, informatif. <c>null</c> si absent.</summary>
    public string? Description { get; }
}
