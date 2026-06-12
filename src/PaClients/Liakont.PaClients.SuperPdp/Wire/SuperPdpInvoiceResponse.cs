namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// La ressource <c>invoice</c> de Super PDP (✅ confirmée OpenAPI v1.24.0.beta + sandbox 2026-06-12 —
/// F14 §3.2/§3.4) : réponse de l'émission (<c>POST /v1.beta/invoices</c>), de la relecture
/// (<c>GET /v1.beta/invoices/{id}</c>) et élément de la liste. DTO PROPRIÉTAIRE, <c>internal</c>.
/// ⚠️ L'envoi est ASYNCHRONE : un HTTP 200 signifie « téléversée » (<c>api:uploaded</c>), JAMAIS
/// « émise » — l'état réel se lit dans <see cref="Events"/> (F14 §4.1, CLAUDE.md n°3).
/// </summary>
internal sealed record SuperPdpInvoiceResponse
{
    /// <summary>Identifiant NUMÉRIQUE attribué par Super PDP, ou <c>null</c> si la création n'a pas abouti.</summary>
    public long? Id { get; init; }

    /// <summary>Identifiant de l'entreprise du compte (audit).</summary>
    public long? CompanyId { get; init; }

    /// <summary>Sens du flux (<c>out</c> = émission).</summary>
    public string? Direction { get; init; }

    /// <summary>
    /// Identifiant externe posé par Liakont à la création (<c>?external_id=</c>, ≤ 36 caractères) — porte
    /// le numéro de document (BT-1) et sert de clé de RACCROCHAGE à la relecture d'idempotence (F14 §4.1).
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// Les événements de traitement (« no formal state machine » — F14 §3.4) : la PRÉSENCE d'un événement
    /// signale qu'il a eu lieu. Le mapper en déduit l'état d'envoi neutre (F14 §4.1).
    /// </summary>
    public IReadOnlyList<SuperPdpInvoiceEvent>? Events { get; init; }
}
