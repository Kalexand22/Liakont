namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Prédicat d'aiguillage PARTAGÉ : un document pivot est une DÉCLARATION e-reporting B2C AGRÉGÉE (flux 10.3)
/// ssi il est marqué déclaration B2C (<see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>) ET porte des
/// frais d'enchères (acheteur OU vendeur). Couvre les DEUX régimes — marge (<see cref="B2cMarginDeclaration"/>,
/// TMA1) et prix total taxable (<see cref="B2cTaxableDeclaration"/>, TLB1) — qui partagent le même invariant :
/// <b>une déclaration B2C agrégée est transmise EXCLUSIVEMENT par un job agrégé (jour×devise×taux), JAMAIS par
/// la voie document</b> (<c>SendDocumentAsync</c> la rejetterait : pas de destinataire identifié). Source de
/// vérité UNIQUE de la garde « différer de la voie document » (SendTenantJob garde D1) ; la discrimination
/// marge ↔ taxable se fait ensuite par <see cref="PivotTotalsDto.TotalTax"/> (== 0 / &gt; 0), côté job.
/// </summary>
public static class B2cAggregatedDeclaration
{
    /// <summary>
    /// Vrai si <paramref name="pivot"/> est une déclaration e-reporting B2C agrégée (marge OU prix total) :
    /// marquée 10.3 par la plateforme ET porteuse de frais acheteur et/ou vendeur (la donnée de calcul agrégée).
    /// </summary>
    /// <param name="pivot">Le document pivot à classer.</param>
    /// <returns><c>true</c> si déclaration B2C agrégée (à différer vers un job agrégé), <c>false</c> sinon (voie document).</returns>
    public static bool Matches(PivotDocumentDto pivot) =>
        pivot != null
        && pivot.IsB2cReportingDeclaration
        && B2cAuctionFeeLines.HasAuctionFees(pivot);
}
