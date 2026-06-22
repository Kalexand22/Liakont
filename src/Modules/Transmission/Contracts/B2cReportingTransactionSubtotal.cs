namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Sous-total de TVA d'une <see cref="B2cReportingTransaction"/>, par taux (bloc DGFiP <c>TaxSubtotal</c>,
/// TG-32 : TT-86 / TT-87 / TT-88). Montants en <see cref="decimal"/> exclusivement (CLAUDE.md n°1).
/// </summary>
public sealed record B2cReportingTransactionSubtotal
{
    /// <summary>Taux de TVA en pourcentage (TT-86), ex. <c>20.0</c>.</summary>
    public required decimal TaxPercent { get; init; }

    /// <summary>Base d'imposition pour ce taux (TT-87) — pour la marge (TMA1) : la marge ramenée HT (G1.57).</summary>
    public required decimal TaxableAmount { get; init; }

    /// <summary>Montant de TVA pour ce taux (TT-88).</summary>
    public required decimal TaxTotal { get; init; }
}
