namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Résultat de la résolution de la contribution de marge d'UN document (<see cref="B2cMarginResolver"/>) :
/// soit résolu (marge TTC + taux unique), soit bloqué (<see cref="BlockReason"/>) — jamais une valeur devinée.
/// </summary>
public sealed record B2cMarginResolution
{
    /// <summary><c>true</c> si la contribution est résolue ; <c>false</c> si bloquée.</summary>
    public required bool IsResolved { get; init; }

    /// <summary>Marge TTC = Σ honoraires (acheteur + vendeur), si résolu (sinon 0).</summary>
    public decimal MarginTtc { get; init; }

    /// <summary>Taux de TVA unique de la vente, si résolu (sinon 0).</summary>
    public decimal RatePercent { get; init; }

    /// <summary>Motif de blocage si <see cref="IsResolved"/> est <c>false</c> ; <c>null</c> sinon.</summary>
    public B2cMarginBlockReason? BlockReason { get; init; }

    /// <summary>Construit une résolution réussie (marge TTC + taux unique).</summary>
    /// <param name="marginTtc">La marge TTC (somme des honoraires).</param>
    /// <param name="ratePercent">Le taux de TVA unique de la vente.</param>
    public static B2cMarginResolution Resolved(decimal marginTtc, decimal ratePercent) =>
        new() { IsResolved = true, MarginTtc = marginTtc, RatePercent = ratePercent };

    /// <summary>Construit un blocage TYPÉ (fail-closed).</summary>
    /// <param name="reason">Le motif de blocage.</param>
    public static B2cMarginResolution Blocked(B2cMarginBlockReason reason) =>
        new() { IsResolved = false, BlockReason = reason };
}
