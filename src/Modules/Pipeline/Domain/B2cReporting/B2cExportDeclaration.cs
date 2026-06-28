namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Prédicat d'aiguillage PARTAGÉ : un document pivot est une DÉCLARATION B2C d'EXPORT HORS UE (flux 10.3,
/// détaxé art. 262 I) ssi il est une déclaration e-reporting B2C agrégée (<see cref="B2cAggregatedDeclaration"/>,
/// marquée + frais) ET reconnu export par <see cref="B2cExportMarking.IsExportDeclaration"/> (toutes lignes
/// catégorie <c>G</c>, art. 262 I — F03 §2.8). À la différence de <see cref="B2cMarginDeclaration"/> (TMA1) et
/// <see cref="B2cTaxableDeclaration"/> (TLB1) qui se partitionnent par <see cref="PivotTotalsDto.TotalTax"/>,
/// l'export PARTAGE le signal <c>TotalTax == 0</c> avec la marge : ils se distinguent par la CATÉGORIE de ligne
/// (<c>G</c> export vs <c>E</c> marge), d'où ce prédicat dédié (et l'exclusion explicite de l'export dans
/// <see cref="B2cMarginDeclaration.Matches"/>). Source de vérité UNIQUE du chemin export (job UNITAIRE
/// <c>B2cExportReportingTenantJob</c>, catégorie de transaction TLB1, taux 0) : le job y appelle CE prédicat,
/// jamais une copie locale. Une divergence casserait l'invariant — un export happé par le job marge (bloqué en
/// 297 E), ou jamais transmis.
/// </summary>
public static class B2cExportDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration B2C d'export hors UE : déclaration B2C agrégée
    /// (<see cref="B2cAggregatedDeclaration.Matches"/>) ET reconnue export (toutes lignes <c>G</c>,
    /// <see cref="B2cExportMarking.IsExportDeclaration"/>). Un document marge (catégorie <c>E</c>) ou taxable
    /// (TVA distincte) est EXCLU ici.
    /// </summary>
    /// <param name="pivot">Le document pivot à classer (ENRICHI par le mapping TVA).</param>
    /// <returns><c>true</c> pour une déclaration B2C d'export hors UE (job export unitaire), <c>false</c> sinon.</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        B2cAggregatedDeclaration.Matches(pivot)
        && B2cExportMarking.IsExportDeclaration(pivot);
}
