namespace Liakont.Modules.FacturX.Infrastructure.Pdf;

/// <summary>
/// Ligne lisible du document (présentation, montants <see cref="decimal"/>). Forme locale (INV-FX-4 :
/// aucune référence au module Archive). Le libellé de TVA est RECOPIÉ du pivot (CLAUDE.md n°2).
/// </summary>
/// <param name="Designation">Désignation de la ligne (EN 16931 BT-153).</param>
/// <param name="Quantity">Quantité facturée (EN 16931 BT-129).</param>
/// <param name="UnitPrice">Prix unitaire net (EN 16931 BT-146), ou <c>null</c> si absent.</param>
/// <param name="NetAmount">Montant HT de la ligne (EN 16931 BT-131).</param>
/// <param name="VatRateLabel">Libellé du taux/catégorie/VATEX, recopié du pivot.</param>
internal sealed record FacturXReadableLine(
    string Designation,
    decimal Quantity,
    decimal? UnitPrice,
    decimal NetAmount,
    string VatRateLabel);
