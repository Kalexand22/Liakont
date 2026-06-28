namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Classification B2C/B2B PARTAGÉE de l'acheteur d'un document d'enchères — source de vérité UNIQUE de
/// l'invariant d'aiguillage : la marge (<see cref="B2cMarginMarking"/>, F03 §2.4) ET le régime du prix total
/// (<see cref="B2cTaxableMarking"/>, F03 §2.7) doivent classer un acheteur de façon IDENTIQUE ; une divergence
/// mis-routerait le document (B2C agrégé vs B2B/voie document). Champs du contrat pivot UNIQUEMENT — jamais
/// <c>Validation.Domain.CompanyHintDetector</c> : le module Pipeline n'accède pas au Domain d'un autre module
/// (frontière P1, CLAUDE.md n°14). Les acheteurs « pseudo-pro » résiduels (forme juridique dans le nom, sans
/// SIREN) sont par ailleurs BLOQUÉS en amont par <c>BuyerLooksProfessionalRule</c> (Validation).
/// </summary>
internal static class B2cBuyerClassification
{
    /// <summary>
    /// Vrai si <paramref name="buyer"/> n'est PAS un professionnel (B2C) : tiers absent, ou aucun indice
    /// professionnel BRUT (SIREN, SIRET, n° TVA, indice société). Conservateur : tout indice → NON B2C (jamais
    /// agrégé en e-reporting B2C à tort — un tel document relève du B2B ou est bloqué en amont par la validation).
    /// </summary>
    /// <param name="buyer">L'acheteur du document (tiers destinataire), éventuellement <c>null</c> (B2C anonyme).</param>
    /// <returns><c>true</c> si l'acheteur est non professionnel (B2C), <c>false</c> sinon.</returns>
    public static bool IsNonProfessional(PivotPartyDto? buyer) =>
        buyer is null
        || (string.IsNullOrWhiteSpace(buyer.Siren)
            && string.IsNullOrWhiteSpace(buyer.Siret)
            && string.IsNullOrWhiteSpace(buyer.VatNumber)
            && !buyer.IsCompanyHint);
}
