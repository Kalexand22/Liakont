namespace Liakont.Modules.Archive.Contracts;

/// <summary>Ligne de ventilation de TVA. Montants en <see cref="decimal"/> (CLAUDE.md n°1).</summary>
/// <param name="VatRateLabel">Libellé du taux ou de l'exonération (« 20 % », « Exonéré »…), fourni par l'appelant.</param>
/// <param name="TaxableBase">Base imposable HT.</param>
/// <param name="TaxAmount">Montant de TVA.</param>
public sealed record ArchiveVatBreakdownLine(string VatRateLabel, decimal TaxableBase, decimal TaxAmount);
