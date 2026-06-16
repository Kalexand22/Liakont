namespace Liakont.Modules.Signature.Application;

using Liakont.Modules.Signature.Domain.Entities;

/// <summary>
/// File DURABLE tenant-scopée des webhooks de signature (ADR-0029 §4 ; INV-YOUSIGN-4/5). L'événement
/// authentifié est persisté AVANT la réponse 2xx (<see cref="EnqueueAsync"/>) ; le drain le traite ensuite en
/// asynchrone (<see cref="DrainPendingAsync"/> → <see cref="MarkProcessedAsync"/>). Idempotence par
/// <c>(company_id, provider_type, event_id)</c> : un rejeu du MÊME événement est sans effet.
/// </summary>
public interface ISignatureWebhookInbox
{
    /// <summary>
    /// Persiste un événement reçu. Renvoie <c>true</c> si l'entrée est nouvelle, <c>false</c> si un événement
    /// de même clé <c>(company_id, provider_type, event_id)</c> existe déjà (rejeu — idempotence à l'inbox).
    /// </summary>
    /// <param name="item">Entrée d'inbox à persister.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<bool> EnqueueAsync(SignatureWebhookInboxItem item, CancellationToken cancellationToken = default);

    /// <summary>Charge jusqu'à <paramref name="max"/> entrées non encore traitées (les plus anciennes d'abord).</summary>
    /// <param name="max">Nombre maximal d'entrées à drainer.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<IReadOnlyList<SignatureWebhookInboxItem>> DrainPendingAsync(
        int max, CancellationToken cancellationToken = default);

    /// <summary>Marque une entrée comme traitée (drain réussi).</summary>
    /// <param name="id">Identifiant de l'entrée.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Incrémente le compteur de tentatives + enregistre la dernière erreur (drain échoué, re-tentable).</summary>
    /// <param name="id">Identifiant de l'entrée.</param>
    /// <param name="errorMessage">Message d'erreur (diagnostic).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken = default);
}
