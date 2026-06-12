namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Une entrée de ventilation de TVA par catégorie/taux (EN 16931 BG-23, schéma <c>vat_break_down</c> de
/// l'OpenAPI — requis : <c>vat_category_taxable_amount</c>, <c>vat_category_tax_amount</c>,
/// <c>vat_category_code</c>). C'est le REGROUPEMENT arithmétique des ventilations de ligne (BG-30) déjà
/// calculées par la source et mappées par la plateforme (F03) : sommes exactes en <see cref="decimal"/>,
/// aucun taux, aucune catégorie, aucun arrondi inventé ici (CLAUDE.md n°1/2) — la validation EN 16931 du
/// converter (BR-S-08, BR-CO-*) rejette toute incohérence de la source plutôt que de l'envoyer fausse.
/// </summary>
internal sealed record SuperPdpEnVatBreakDown
{
    /// <summary>Base taxable de la catégorie (EN 16931 BT-116) — somme des montants nets des lignes du groupe.</summary>
    public required decimal VatCategoryTaxableAmount { get; init; }

    /// <summary>Montant de TVA de la catégorie (EN 16931 BT-117) — somme des TVA de ligne du groupe.</summary>
    public required decimal VatCategoryTaxAmount { get; init; }

    /// <summary>Catégorie UNCL5305 (EN 16931 BT-118), ex. <c>S</c>, <c>E</c> — recopiée du pivot (F03).</summary>
    public required string VatCategoryCode { get; init; }

    /// <summary>Taux de TVA en pourcentage (EN 16931 BT-119), ou <c>null</c> si non porté par le pivot.</summary>
    public decimal? VatCategoryRate { get; init; }

    /// <summary>Code VATEX du motif d'exonération (EN 16931 BT-121), recopié du pivot (catégorie E — F03 §2.2).</summary>
    public string? VatExemptionReasonCode { get; init; }
}
