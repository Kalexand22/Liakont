namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Prédicat d'aiguillage PARTAGÉ (B4) : un document pivot est une DÉCLARATION DE MARGE B2C ssi il est une
/// déclaration e-reporting B2C agrégée (<see cref="B2cAggregatedDeclaration"/>) ET sous le régime de la MARGE
/// (aucune TVA distincte au grain document, <see cref="PivotTotalsDto.TotalTax"/> == 0 — art. 297 E, F03 §2.3).
/// La déclinaison SYMÉTRIQUE pour le régime du prix total (TVA distincte) est <see cref="B2cTaxableDeclaration"/>
/// (TLB1, F03 §2.7) ; ensemble elles PARTITIONNENT les déclarations B2C agrégées par <c>TotalTax</c>. Source de
/// vérité UNIQUE du chemin marge (job agrégé <c>B2cMarginAggregatorTenantJob</c>) : le job y appelle CE prédicat,
/// jamais une copie locale. Une divergence casserait l'invariant — un document taxable happé par le job marge
/// (bloqué en 297 E), ou jamais transmis.
/// </summary>
public static class B2cMarginDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration de marge B2C : déclaration B2C agrégée
    /// (<see cref="B2cAggregatedDeclaration.Matches"/>) ET sans TVA distincte (<see cref="PivotTotalsDto.TotalTax"/>
    /// == 0, art. 297 E) — le régime de la marge. Un document à TVA distincte (prix total taxable) est EXCLU ici
    /// (il relève de <see cref="B2cTaxableDeclaration"/>).
    /// </summary>
    /// <param name="pivot">Le document pivot à classer.</param>
    /// <returns><c>true</c> pour une déclaration de marge B2C (job agrégé marge), <c>false</c> sinon.</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        B2cAggregatedDeclaration.Matches(pivot)
        && pivot.Totals.TotalTax == 0m;
}
