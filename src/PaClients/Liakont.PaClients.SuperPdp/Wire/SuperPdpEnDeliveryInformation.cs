namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Informations de livraison du schéma <c>en_invoice</c> de Super PDP (<c>delivery_information</c>,
/// OpenAPI v1.24.0.beta) — EN 16931 BG-13. V1 ne porte que la <c>delivery_date</c> (BT-72) : aux enchères
/// la livraison du lot intervient à l'adjudication (= date de vente). Le builder l'émet TOUJOURS (date du
/// pivot, sinon la date d'émission) pour que l'élément livraison du CII produit par le converter ne soit
/// JAMAIS vide (<c>PEPPOL-EN16931-R008</c>, F16 §3.5).
/// </summary>
internal sealed record SuperPdpEnDeliveryInformation
{
    /// <summary>Date de livraison effective au format <c>yyyy-MM-dd</c> (EN 16931 BT-72), sérialisée <c>delivery_date</c>.</summary>
    public required string DeliveryDate { get; init; }
}
