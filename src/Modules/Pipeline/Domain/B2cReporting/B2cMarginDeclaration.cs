namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Prédicat d'aiguillage PARTAGÉ (B4) : un document pivot est une DÉCLARATION DE MARGE B2C ssi il est marqué
/// déclaration d'e-reporting B2C (flux 10.3, <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>) ET porte
/// des frais de marge (acheteur OU vendeur). Source de vérité UNIQUE de l'invariant d'aiguillage
/// « marge = job agrégé (<c>B2cMarginAggregatorTenantJob</c>) ⊕ taxable = voie document (<c>SendTenantJob</c>) » :
/// les deux côtés appellent CE prédicat, jamais une copie locale. Une divergence (un seul côté modifié)
/// casserait l'invariant — double-transmission, ou document jamais transmis.
/// </summary>
public static class B2cMarginDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration de marge B2C : déclaration 10.3 marquée par la
    /// plateforme ET porteuse de frais acheteur et/ou vendeur (la donnée de calcul de la marge).
    /// </summary>
    /// <param name="pivot">Le document pivot à classer.</param>
    /// <returns><c>true</c> pour une déclaration de marge B2C (chemin job agrégé), <c>false</c> sinon (voie document).</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        pivot != null
        && pivot.IsB2cReportingDeclaration
        && (((pivot.SellerFees?.Count ?? 0) > 0) || ((pivot.BuyerFees?.Count ?? 0) > 0));
}
