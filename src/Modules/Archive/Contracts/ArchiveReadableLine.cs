namespace Liakont.Modules.Archive.Contracts;

/// <summary>Ligne d'un document pour le rendu lisible. Montants en <see cref="decimal"/> (CLAUDE.md n°1).</summary>
/// <param name="Designation">Désignation de la ligne.</param>
/// <param name="Quantity">Quantité, ou <c>null</c> si absente.</param>
/// <param name="UnitPrice">Prix unitaire HT, ou <c>null</c> si absent.</param>
/// <param name="NetAmount">Montant HT de la ligne.</param>
/// <param name="VatRateLabel">Libellé du taux/exonération de la ligne (fourni par l'appelant), ou <c>null</c>.</param>
public sealed record ArchiveReadableLine(
    string Designation,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal NetAmount,
    string? VatRateLabel);
