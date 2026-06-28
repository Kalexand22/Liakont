namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Un honoraire (acheteur OU vendeur) d'un document B2C-marge, avec son taux DÉJÀ RÉSOLU par le mapping F03
/// (<c>null</c> = code TVA source non mappé → le document sera bloqué, fail-closed). Montant <b>TTC</b>
/// (nature enchères, F03 §2.5). Entrée PURE de <see cref="B2cMarginResolver"/> (la résolution du taux, I/O,
/// est faite par l'appelant tenant-scopé).
/// </summary>
public sealed record B2cResolvedHonoraire
{
    /// <summary>Montant TTC de l'honoraire (<see cref="decimal"/>, CLAUDE.md n°1).</summary>
    public required decimal AmountTtc { get; init; }

    /// <summary>Taux de TVA résolu (mapping F03, pourcentage), ou <c>null</c> si le code TVA source n'est pas mappé.</summary>
    public required decimal? RatePercent { get; init; }
}
