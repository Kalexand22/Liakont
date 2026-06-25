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
/// <para>⚠️ L'EXPORT HORS UE détaxé (<see cref="B2cExportDeclaration"/>, art. 262 I) partage le signal
/// <c>TotalTax == 0</c> de la marge (il est exonéré, pas de TVA distincte) : il est EXPLICITEMENT EXCLU ici par
/// <see cref="B2cExportMarking.IsExportDeclaration"/> (catégorie de ligne <c>G</c> ≠ <c>E</c> marge), sinon le job
/// marge le ramasserait et le bloquerait en 297 E. C'est le SEUL recouvrement entre les trois chemins B2C.</para>
/// </summary>
public static class B2cMarginDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration de marge B2C : déclaration B2C agrégée
    /// (<see cref="B2cAggregatedDeclaration.Matches"/>) ET sans TVA distincte (<see cref="PivotTotalsDto.TotalTax"/>
    /// == 0, art. 297 E) ET PAS un export hors UE (<see cref="B2cExportMarking.IsExportDeclaration"/>, catégorie
    /// <c>G</c>) — le régime de la marge. Un document à TVA distincte (prix total taxable, relève de
    /// <see cref="B2cTaxableDeclaration"/>) ou un export détaxé (relève de <see cref="B2cExportDeclaration"/>) est
    /// EXCLU ici.
    /// </summary>
    /// <param name="pivot">Le document pivot à classer (ENRICHI par le mapping TVA).</param>
    /// <returns><c>true</c> pour une déclaration de marge B2C (job agrégé marge), <c>false</c> sinon.</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        B2cAggregatedDeclaration.Matches(pivot)
        && pivot.Totals.TotalTax == 0m
        && !B2cExportMarking.IsExportDeclaration(pivot);
}
