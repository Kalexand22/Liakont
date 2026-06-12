namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Ventilation de TVA d'une ligne (EN 16931 BG-30, schéma <c>line_vat_information</c> de l'OpenAPI).
/// La catégorie UNCL5305 et le taux sont le RÉSULTAT du mapping TVA de la PLATEFORME (F03) porté par le
/// pivot — jamais inventés ici (CLAUDE.md n°2) ; le motif d'exonération (VATEX) remonte au niveau
/// document (<see cref="SuperPdpEnVatBreakDown.VatExemptionReasonCode"/>, EN 16931 BT-121).
/// </summary>
internal sealed record SuperPdpEnLineVatInformation
{
    /// <summary>Catégorie UNCL5305 de la ligne (EN 16931 BT-151), ex. <c>S</c>, <c>E</c>.</summary>
    public required string InvoicedItemVatCategoryCode { get; init; }

    /// <summary>Taux de TVA de la ligne en pourcentage (EN 16931 BT-152), ou <c>null</c> si non porté.</summary>
    public decimal? InvoicedItemVatRate { get; init; }
}
