namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Résultat de la résolution de marge d'UN document (<see cref="B2cMarginDocumentResolver"/>), avec la ventilation
/// acheteur/vendeur (pour le récap du détail document) EN PLUS de la marge TTC + taux unique (consommés par
/// l'agrégat e-reporting B2C). Bloqué (fail-closed, <see cref="BlockReason"/>) si la marge n'est pas résolvable
/// (TVA distincte, aucun honoraire, taux non mappé, taux mixtes) — jamais une valeur devinée (CLAUDE.md n°2/3).
/// </summary>
public sealed record B2cMarginDocumentResolution
{
    /// <summary><c>true</c> si la marge est résolue ; <c>false</c> si bloquée.</summary>
    public required bool IsResolved { get; init; }

    /// <summary>Commission ACHETEUR TTC (Σ lignes rôle BuyerFee), arrondie ; renseignée même si bloqué.</summary>
    public decimal BuyerFeesTtc { get; init; }

    /// <summary>Commission VENDEUR TTC (Σ <c>SellerFees</c>, décompte BV), arrondie ; renseignée même si bloqué.</summary>
    public decimal SellerFeesTtc { get; init; }

    /// <summary>Marge TTC = Σ honoraires (acheteur + vendeur), si résolu (sinon 0).</summary>
    public decimal MarginTtc { get; init; }

    /// <summary>Taux de TVA unique de la vente, si résolu (sinon 0).</summary>
    public decimal RatePercent { get; init; }

    /// <summary>Motif de blocage si <see cref="IsResolved"/> est <c>false</c> ; <c>null</c> sinon.</summary>
    public B2cMarginBlockReason? BlockReason { get; init; }
}
