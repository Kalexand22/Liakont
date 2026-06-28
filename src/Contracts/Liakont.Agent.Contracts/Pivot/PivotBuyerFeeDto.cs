namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Frais ACHETEUR (commission acheteur) porté dans le pivot comme DONNÉE DE CALCUL de la marge
/// e-reporting B2C (B2C-08c, F03 §2.4) — 2e jambe de la formule sourcée
/// <c>marge = Σ frais acheteur + Σ frais vendeur</c>. Symétrique de <see cref="PivotSellerFeeDto"/>,
/// augmenté de <see cref="SourceTaxAmount"/> (TVA de frais BRUTE, requise pour recouvrer la base HT d'un export).
/// DTO PUR : aucun calcul, aucune règle fiscale ; il transporte le terme extrait par la source (TTC en enchères, F03 §2.5 ; nature HT/TTC = paramétrage) (côté
/// agent : le <c>type_ligne 2</c> « frais acheteur » d'EncheresV6, déjà lu) au grain lot, pour que la
/// plateforme calcule la marge (B2C-09b).
/// <para>
/// Ce n'est JAMAIS une ligne facturée à l'acheteur ni une ventilation de TVA imputée au document : sous le
/// régime de la marge (CGI art. 297 E) aucune TVA distincte n'apparaît au flux 10.3, et le frais acheteur ne
/// gonfle pas la base imposable du document 10.3 (B2C-08c, règle n°2). D'où l'ABSENCE de ventilation de TVA
/// (pas de <c>Rate</c>, catégorie ni VATEX). Il est porté hors de <see cref="PivotDocumentDto.Lines"/> et
/// n'impacte pas <see cref="PivotTotalsDto"/>.
/// </para>
/// <para>
/// <see cref="SourceTaxAmount"/> N'EST PAS une ventilation de TVA de la marge : c'est le montant de TVA de
/// frais tel qu'il existe à la SOURCE (brut, transporté sans logique), nécessaire à la SEULE plateforme pour
/// recouvrer la base HT exonérée d'un export hors UE/intracom (F03 §2.8, <c>base = NetAmount − SourceTaxAmount</c>) ;
/// sous un export il vaut 0 (commission détaxée) et le champ est alors OMIS. Les flux marge/taxable l'ignorent
/// (ils raisonnent sur le TTC <see cref="NetAmount"/>).
/// </para>
/// Montant en <see cref="decimal"/> (CLAUDE.md n°1). Le code régime est transporté BRUT (le mapping F03
/// est plateforme — jamais interprété ici, CLAUDE.md n°2).
/// </summary>
public sealed class PivotBuyerFeeDto
{
    /// <summary>Crée un frais acheteur pivot (donnée de calcul de marge, grain lot).</summary>
    /// <param name="lotReference">
    /// Identifiant du lot de rattachement (côté EncheresV6 : le bordereau <c>no_ba</c>) — clé au grain lot.
    /// </param>
    /// <param name="netAmount">Montant TTC du frais acheteur (nature TTC en enchères, F03 §2.5), en <c>decimal</c> (arrondi half-up par la source) — terme de la marge.</param>
    /// <param name="sourceRegimeCode">Code régime TVA source de la ligne, BRUT (jamais interprété — CLAUDE.md n°2). <c>null</c> si absent.</param>
    /// <param name="sourceLineRef">Référence de la ligne dans le système source, pour la traçabilité. <c>null</c> si absent.</param>
    /// <param name="description">Libellé de la ligne source, informatif. <c>null</c> si absent.</param>
    /// <param name="sourceTaxAmount">Montant de TVA de frais SOURCE (brut, jamais calculé/interprété — CLAUDE.md n°2), inclus dans <paramref name="netAmount"/> (TTC). <c>null</c> quand la source ne porte aucune TVA de frais (commission exonérée d'un export, F03 §2.8). Sert à la plateforme à recouvrer la base HT exonérée (<c>base = NetAmount − SourceTaxAmount</c>).</param>
    public PivotBuyerFeeDto(
        string lotReference,
        decimal netAmount,
        string? sourceRegimeCode = null,
        string? sourceLineRef = null,
        string? description = null,
        decimal? sourceTaxAmount = null)
    {
        LotReference = lotReference;
        NetAmount = netAmount;
        SourceRegimeCode = sourceRegimeCode;
        SourceLineRef = sourceLineRef;
        Description = description;
        SourceTaxAmount = sourceTaxAmount;
    }

    /// <summary>Identifiant du lot de rattachement (grain lot — côté EncheresV6 : <c>no_ba</c>).</summary>
    public string LotReference { get; }

    /// <summary>Montant TTC du frais acheteur (nature TTC en enchères, F03 §2.5), en <c>decimal</c> — terme de la formule de marge (B2C-09b).</summary>
    public decimal NetAmount { get; }

    /// <summary>Code régime TVA source de la ligne, BRUT (jamais mappé/interprété — CLAUDE.md n°2). <c>null</c> si absent.</summary>
    public string? SourceRegimeCode { get; }

    /// <summary>Référence de la ligne dans le système source, pour la traçabilité. <c>null</c> si absent.</summary>
    public string? SourceLineRef { get; }

    /// <summary>Libellé de la ligne source, informatif. <c>null</c> si absent.</summary>
    public string? Description { get; }

    /// <summary>
    /// Montant de TVA de frais SOURCE (brut, inclus dans <see cref="NetAmount"/> TTC), jamais
    /// calculé/interprété ici (CLAUDE.md n°2). <c>null</c> = aucune TVA de frais à la source (commission
    /// exonérée d'un export, F03 §2.8). Champ ADDITIF en fin (ADR-0007) ; la plateforme s'en sert pour
    /// recouvrer la base HT exonérée (<c>base = NetAmount − SourceTaxAmount</c>) — jamais une ventilation
    /// de TVA imputée au flux 10.3.
    /// </summary>
    public decimal? SourceTaxAmount { get; }
}
