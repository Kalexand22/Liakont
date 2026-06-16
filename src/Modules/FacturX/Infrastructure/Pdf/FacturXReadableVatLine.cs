namespace Liakont.Modules.FacturX.Infrastructure.Pdf;

/// <summary>
/// Ligne de ventilation de TVA (BG-23) affichée dans le rendu visuel. Forme locale (INV-FX-4). Les
/// montants sont en <see cref="decimal"/> ; le libellé est recopié du pivot (CLAUDE.md n°1/2).
/// </summary>
/// <param name="VatRateLabel">Libellé du taux/catégorie/VATEX, recopié du pivot.</param>
/// <param name="TaxableBase">Base imposable HT du groupe.</param>
/// <param name="TaxAmount">Montant de TVA du groupe.</param>
internal sealed record FacturXReadableVatLine(
    string VatRateLabel,
    decimal TaxableBase,
    decimal TaxAmount);
