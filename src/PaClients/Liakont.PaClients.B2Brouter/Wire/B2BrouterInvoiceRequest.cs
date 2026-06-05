namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Enveloppe du payload d'import d'une facture B2Brouter — <c>POST /accounts/{accountId}/invoices.json</c>
/// attend <c>{ "invoice": { … } }</c> (F05 §2). DTO PROPRIÉTAIRE B2Brouter, <c>internal</c> : il ne
/// fuit jamais hors de l'assembly (acceptance PAB01, B2BrouterBoundaryTests). Sérialisé en
/// snake_case (<see cref="B2BrouterJson"/>).
/// </summary>
internal sealed record B2BrouterInvoiceRequest
{
    /// <summary>La facture à importer.</summary>
    public required B2BrouterInvoice Invoice { get; init; }
}
