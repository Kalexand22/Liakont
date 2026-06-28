namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;

/// <summary>
/// Résultat de la résolution d'UN document B2C taxable (<see cref="B2cTaxableResolver"/>, F03 §2.7) : soit
/// RÉSOLU (composantes par taux <see cref="Components"/>), soit BLOQUÉ (<see cref="BlockReason"/>). Fail-closed :
/// un document non résolu n'est jamais agrégé ni transmis (CLAUDE.md n°2/3).
/// </summary>
public sealed record B2cTaxableResolution
{
    private B2cTaxableResolution(bool isResolved, IReadOnlyList<B2cTaxableRateComponent>? components, B2cTaxableBlockReason? blockReason)
    {
        IsResolved = isResolved;
        Components = components;
        BlockReason = blockReason;
    }

    /// <summary><c>true</c> si la résolution a abouti ; <c>false</c> si bloquée.</summary>
    public bool IsResolved { get; }

    /// <summary>Composantes par taux (≥ 1) si résolu ; <c>null</c> si bloqué.</summary>
    public IReadOnlyList<B2cTaxableRateComponent>? Components { get; }

    /// <summary>Motif de blocage si bloqué ; <c>null</c> si résolu.</summary>
    public B2cTaxableBlockReason? BlockReason { get; }

    /// <summary>Résolution réussie portant les composantes par taux.</summary>
    /// <param name="components">Les composantes par taux (au moins une).</param>
    /// <returns>La résolution résolue.</returns>
    public static B2cTaxableResolution Resolved(IReadOnlyList<B2cTaxableRateComponent> components) =>
        new(isResolved: true, components, blockReason: null);

    /// <summary>Résolution bloquée (fail-closed) portant le motif.</summary>
    /// <param name="reason">Le motif de blocage.</param>
    /// <returns>La résolution bloquée.</returns>
    public static B2cTaxableResolution Blocked(B2cTaxableBlockReason reason) =>
        new(isResolved: false, components: null, reason);
}
