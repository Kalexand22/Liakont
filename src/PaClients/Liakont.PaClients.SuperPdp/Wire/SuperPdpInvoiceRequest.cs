namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Enveloppe du payload d'émission d'une facture Super PDP — <c>POST /v1.beta/invoices</c> (🟠 cible de
/// conception F14 §3.2, à confirmer OpenAPI sandbox PAS03). DTO PROPRIÉTAIRE Super PDP, <c>internal</c> :
/// il ne fuit jamais hors de l'assembly (acceptance PAS02, SuperPdpBoundaryTests). Sérialisé en
/// snake_case (<see cref="SuperPdpJson"/>).
/// </summary>
internal sealed record SuperPdpInvoiceRequest
{
    /// <summary>La facture à émettre.</summary>
    public required SuperPdpInvoice Invoice { get; init; }
}
