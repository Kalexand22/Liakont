namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Prédicat d'aiguillage PARTAGÉ : un document pivot est une DÉCLARATION B2C d'un document ORDINAIRE taxable
/// (flux 10.3, F03 §2.9 — facture client / note d'honoraires) ssi il est marqué déclaration B2C
/// (<see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>) ET ne porte AUCUN frais d'enchères. C'est le
/// COMPLÉMENT exact de <see cref="B2cAggregatedDeclaration"/> (marqué + frais) au sein des déclarations B2C
/// marquées : marqué SANS frais = document ordinaire (job <c>B2cPlainTaxableReportingTenantJob</c>, catégorie
/// TLB1/TPS1 selon <see cref="PivotDocumentDto.OperationCategory"/>) ; marqué AVEC frais = bordereau d'enchères
/// agrégé (marge/taxable/export). Source de vérité UNIQUE du chemin ordinaire : le job y appelle CE prédicat,
/// jamais une copie locale. La TVA distincte (<c>TotalTax &gt; 0</c>) est garantie par le marquage qui a posé le
/// flag (<see cref="B2cPlainTaxableMarking"/>) — un document ordinaire marqué est toujours taxable.
/// </summary>
public static class B2cPlainTaxableDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration B2C d'un document ordinaire taxable : marqué 10.3
    /// (<see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>) ET SANS frais acheteur/vendeur (≠ bordereau
    /// d'enchères). Un bordereau d'enchères (porteur de frais) est EXCLU ici (il relève de
    /// <see cref="B2cAggregatedDeclaration"/>).
    /// </summary>
    /// <param name="pivot">Le document pivot à classer.</param>
    /// <returns><c>true</c> pour une déclaration B2C ordinaire taxable (job ordinaire), <c>false</c> sinon.</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        pivot != null
        && pivot.IsB2cReportingDeclaration
        && ((pivot.SellerFees?.Count ?? 0) == 0)
        && ((pivot.BuyerFees?.Count ?? 0) == 0);
}
