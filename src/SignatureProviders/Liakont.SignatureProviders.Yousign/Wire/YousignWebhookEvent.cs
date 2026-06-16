namespace Liakont.SignatureProviders.Yousign.Wire;

/// <summary>
/// Événement de webhook Yousign v3 (champs strictement nécessaires au routage/idempotence). Type « fil »
/// INTERNE : il est désérialisé du RAW body APRÈS vérification HMAC, jamais avant (ADR-0029 §3/§4). Ne
/// traverse jamais l'abstraction (INV-YOUSIGN-2).
/// </summary>
internal sealed record YousignWebhookEvent
{
    /// <summary>Identifiant d'événement fourni par Yousign, ou <c>null</c> (un surrogate stable est alors dérivé).</summary>
    public string? EventId { get; init; }

    /// <summary>Nom de l'événement (ex. <c>signature_request.done</c>, <c>signature_request.declined</c>).</summary>
    public string? EventName { get; init; }

    /// <summary>Bloc de données de l'événement.</summary>
    public YousignWebhookData? Data { get; init; }

    /// <summary>
    /// Identifiant d'événement EFFECTIF pour l'idempotence (ADR-0029 §4) : l'<see cref="EventId"/> fourni
    /// par Yousign s'il existe, sinon un surrogate STABLE dérivé de (nom d'événement, référence de demande,
    /// statut) — un même événement logique rejoué produit la même clé ; deux événements distincts (signer.done
    /// vs request.done) produisent des clés distinctes.
    /// </summary>
    public string ResolveEventId()
    {
        if (!string.IsNullOrWhiteSpace(EventId))
        {
            return EventId.Trim();
        }

        var reference = Data?.SignatureRequest?.Id ?? "?";
        var name = EventName ?? "?";
        var status = Data?.SignatureRequest?.Status ?? "?";
        return $"{name}:{reference}:{status}";
    }
}
