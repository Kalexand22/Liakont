namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Issue du traitement d'un webhook de complétion (<see cref="SignatureWebhookResult"/>).
/// </summary>
public enum SignatureWebhookState
{
    /// <summary>Événement ACCEPTÉ et pris en compte.</summary>
    Accepted,

    /// <summary>Événement IGNORÉ (déjà traité — idempotence — ou non pertinent).</summary>
    Ignored,

    /// <summary>Événement REJETÉ (signature HMAC invalide).</summary>
    Rejected,

    /// <summary>Le fournisseur ne gère pas les webhooks (flag <see cref="CompletionTransport.Webhook"/> absent).</summary>
    CapabilityNotSupported,
}
