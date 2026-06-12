namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Un événement de traitement d'une facture (schéma <c>event</c> de l'OpenAPI — requis : <c>id</c>,
/// <c>invoice_id</c>, <c>status_code</c>, <c>status_text</c>, <c>created_at</c>). Les familles de
/// <c>status_code</c> (<c>api:*</c> internes, <c>fr:*</c> cycle de vie officiel français, <c>ppf:*</c>
/// dépôts PPF) sont documentées dans l'OpenAPI et mappées en F14 §4.1.
/// </summary>
internal sealed record SuperPdpInvoiceEvent
{
    /// <summary>Code de statut (ex. <c>api:uploaded</c>, <c>fr:201</c>).</summary>
    public string? StatusCode { get; init; }

    /// <summary>Libellé du statut tel que fourni par Super PDP (souvent en français).</summary>
    public string? StatusText { get; init; }

    /// <summary>Horodatage de l'événement.</summary>
    public string? CreatedAt { get; init; }
}
