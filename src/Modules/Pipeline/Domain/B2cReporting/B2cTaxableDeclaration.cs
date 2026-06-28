namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Prédicat d'aiguillage PARTAGÉ : un document pivot est une DÉCLARATION B2C au RÉGIME DU PRIX TOTAL (taxable)
/// ssi il est une déclaration e-reporting B2C agrégée (<see cref="B2cAggregatedDeclaration"/>) ET porte une TVA
/// distincte au grain document (<see cref="PivotTotalsDto.TotalTax"/> &gt; 0 — art. 297 E ne s'applique pas,
/// F03 §2.7). Symétrique de <see cref="B2cMarginDeclaration"/> (régime de la marge, <c>TotalTax == 0</c>) ;
/// ensemble elles PARTITIONNENT les déclarations B2C agrégées. Source de vérité UNIQUE du chemin taxable (job
/// agrégé <c>B2cTaxableAggregatorTenantJob</c>, catégorie de transaction TLB1) : le job y appelle CE prédicat,
/// jamais une copie locale. Une divergence casserait l'invariant — un document marge happé par le job taxable,
/// ou jamais transmis.
/// </summary>
public static class B2cTaxableDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration B2C au régime du prix total : déclaration B2C agrégée
    /// (<see cref="B2cAggregatedDeclaration.Matches"/>) ET à TVA distincte (<see cref="PivotTotalsDto.TotalTax"/>
    /// &gt; 0). Un document marge (sans TVA distincte) est EXCLU ici (il relève de <see cref="B2cMarginDeclaration"/>).
    /// </summary>
    /// <param name="pivot">Le document pivot à classer.</param>
    /// <returns><c>true</c> pour une déclaration B2C taxable (job agrégé taxable), <c>false</c> sinon.</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        B2cAggregatedDeclaration.Matches(pivot)
        && pivot.Totals.TotalTax > 0m;
}
