namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Résultat du traitement d'un webhook de complétion (ADR-0027 §2/§3). Un fournisseur SANS le flag
/// <see cref="CompletionTransport.Webhook"/> (ex. capteur sur place <see cref="CompletionTransport.Synchronous"/>)
/// renvoie <see cref="State"/> = <see cref="SignatureWebhookState.CapabilityNotSupported"/> — jamais une
/// exception (INV-SIGPROV-3/5).
/// </summary>
public sealed record SignatureWebhookResult
{
    /// <summary>Issue du traitement du webhook.</summary>
    public required SignatureWebhookState State { get; init; }

    /// <summary>Référence côté fournisseur concernée par l'événement, ou <c>null</c>.</summary>
    public string? ProviderReference { get; init; }

    /// <summary>
    /// Identifiant d'ÉVÉNEMENT côté fournisseur, ou <c>null</c>. Porte la clé d'idempotence de l'inbox durable
    /// avec le tenant et le type de fournisseur — <c>(company_id, provider_type, event_id)</c>, jamais
    /// <c>event_id</c> seul (deux tenants/providers peuvent partager un identifiant — SIG07, ADR-0029 §4).
    /// Renseigné sur un événement <see cref="SignatureWebhookState.Accepted"/> par les plug-ins à webhook.
    /// </summary>
    public string? EventId { get; init; }

    /// <summary>Détail de la capacité absente si le webhook n'est pas pertinent pour ce fournisseur ; <c>null</c> sinon.</summary>
    public SignatureCapabilityNotSupportedResult? CapabilityNotSupported { get; init; }

    /// <summary>Construit un résultat « webhook accepté » (événement pris en compte).</summary>
    /// <param name="providerReference">Référence côté fournisseur (facultatif).</param>
    /// <param name="eventId">Identifiant d'événement pour l'idempotence de l'inbox (facultatif — SIG07).</param>
    public static SignatureWebhookResult Accepted(string? providerReference = null, string? eventId = null) => new()
    {
        State = SignatureWebhookState.Accepted,
        ProviderReference = providerReference,
        EventId = eventId,
    };

    /// <summary>Construit un résultat « webhook ignoré » (déjà traité — idempotence, ou non pertinent).</summary>
    public static SignatureWebhookResult Ignored() => new()
    {
        State = SignatureWebhookState.Ignored,
    };

    /// <summary>Construit un résultat « webhook rejeté » (signature HMAC invalide — SIG07).</summary>
    public static SignatureWebhookResult Rejected() => new()
    {
        State = SignatureWebhookState.Rejected,
    };

    /// <summary>Construit un résultat « webhook non pertinent : capacité absente » (jamais d'exception).</summary>
    /// <param name="capabilityGap">Détail journalisable de la capacité manquante.</param>
    public static SignatureWebhookResult NotSupported(SignatureCapabilityNotSupportedResult capabilityGap) => new()
    {
        State = SignatureWebhookState.CapabilityNotSupported,
        CapabilityNotSupported = capabilityGap,
    };
}
