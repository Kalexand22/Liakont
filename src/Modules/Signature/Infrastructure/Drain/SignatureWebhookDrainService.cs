namespace Liakont.Modules.Signature.Infrastructure.Drain;

using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

/// <summary>
/// Drain de l'inbox de webhooks de signature pour le tenant courant (ADR-0029 §5 ; INV-YOUSIGN-6). Lit les
/// événements non traités, RÉSOUT le fournisseur par capacités (jamais un <c>if (provider is Yousign)</c>),
/// télécharge la preuve, et la RAPATRIE dans le coffre WORM via <see cref="IArchiveService"/> (Contracts) —
/// JAMAIS le plug-in, JAMAIS <c>Archive.Domain</c> (frontière NetArchTest). At-least-once : la preuve est
/// archivée AVANT le marquage « traité » ; le coffre étant append-only chaîné, un éventuel rejeu après crash
/// ajoute un addendum redondant (jamais une corruption ni une réécriture WORM). L'idempotence d'INGESTION
/// (un événement n'entre qu'une fois) est garantie par l'inbox (clé <c>(company_id, provider_type, event_id)</c>).
/// </summary>
internal sealed partial class SignatureWebhookDrainService : ISignatureWebhookDrainService
{
    private const int BatchSize = 100;

    private readonly ISignatureWebhookInbox _inbox;
    private readonly ISignatureRequestStore _requests;
    private readonly ISignatureAccountStore _accounts;
    private readonly ISignatureProviderRegistry _registry;
    private readonly IArchiveService _archive;
    private readonly ILogger<SignatureWebhookDrainService> _logger;

    public SignatureWebhookDrainService(
        ISignatureWebhookInbox inbox,
        ISignatureRequestStore requests,
        ISignatureAccountStore accounts,
        ISignatureProviderRegistry registry,
        IArchiveService archive,
        ILogger<SignatureWebhookDrainService> logger)
    {
        _inbox = inbox;
        _requests = requests;
        _accounts = accounts;
        _registry = registry;
        _archive = archive;
        _logger = logger;
    }

    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _inbox.DrainPendingAsync(BatchSize, cancellationToken).ConfigureAwait(false);
        var processed = 0;

        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var handled = await ProcessAsync(item, cancellationToken).ConfigureAwait(false);
                if (handled)
                {
                    await _inbox.MarkProcessedAsync(item.Id, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Échec isolé : l'entrée reste non traitée (re-tentable au prochain drain) ; on n'avale rien.
                LogDrainItemFailed(_logger, item.Id, ex.Message);
                await _inbox.MarkFailedAsync(item.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        return processed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Preuve de signature rapatriée en WORM (référence {Reference}, document {DocumentId}).")]
    private static partial void LogProofArchived(ILogger logger, string reference, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook de signature orphelin ignoré (référence {Reference} sans demande connue).")]
    private static partial void LogOrphanEvent(ILogger logger, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Preuve de signature indisponible (référence {Reference}) : {Reason}.")]
    private static partial void LogProofUnavailable(ILogger logger, string reference, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drain d'un webhook de signature échoué (entrée {ItemId}) : {Error}.")]
    private static partial void LogDrainItemFailed(ILogger logger, Guid itemId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook de signature mis de côté après {Attempts} tentatives (entrée {ItemId}) : preuve toujours indisponible.")]
    private static partial void LogDeadLettered(ILogger logger, int attempts, Guid itemId);

    // Traite UNE entrée. Renvoie true si l'entrée est définitivement traitée (à marquer), false si elle doit
    // rester en attente pour un prochain drain (preuve pas encore disponible).
    private async Task<bool> ProcessAsync(
        Domain.Entities.SignatureWebhookInboxItem item, CancellationToken cancellationToken)
    {
        var link = await _requests
            .GetByProviderReferenceAsync(item.CompanyId, item.ProviderType, item.ProviderReference, cancellationToken)
            .ConfigureAwait(false);

        if (link is null)
        {
            // Événement orphelin (demande inconnue/supprimée) : rien à rapatrier → traité (jamais bloquant).
            LogOrphanEvent(_logger, item.ProviderReference);
            return true;
        }

        var account = await _accounts
            .GetActiveAccountAsync(item.CompanyId, item.ProviderType, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            throw new InvalidOperationException(
                $"Compte de signature « {item.ProviderType} » introuvable pour le tenant {item.CompanyId} : "
                + "impossible de télécharger la preuve. Vérifiez le paramétrage du compte.");
        }

        var provider = _registry.Resolve(account);
        var proof = await provider.DownloadProofAsync(item.ProviderReference, cancellationToken).ConfigureAwait(false);

        if (proof.Content is null)
        {
            // Capacité absente ou preuve pas encore disponible : laisser en attente (re-tentable au prochain drain).
            LogProofUnavailable(_logger, item.ProviderReference, proof.CapabilityNotSupported?.OperatorMessage ?? "indisponible");
            if (item.AttemptCount + 1 >= PostgresSignatureWebhookInbox.MaxDrainAttempts)
            {
                LogDeadLettered(_logger, item.AttemptCount + 1, item.Id);
            }

            await _inbox.MarkFailedAsync(item.Id, "Preuve indisponible (re-tentable).", cancellationToken).ConfigureAwait(false);
            return false;
        }

        // Rapatriement WORM via Archive.Contracts (jamais le plug-in, jamais Archive.Domain — INV-YOUSIGN-6).
        var addendum = new ArchiveAddendumRequest
        {
            DocumentId = link.DocumentId,
            DocumentNumber = link.DocumentNumber,
            IssueDate = link.IssueDate,
            Kind = $"signature-{item.ProviderType.ToLowerInvariant()}",
            Attachment = new ArchiveAttachment(
                FileName: $"{link.DocumentNumber}-signature-proof",
                ContentType: proof.ContentType ?? "application/pdf",
                Content: proof.Content as byte[] ?? proof.Content.ToArray()),
        };

        await _archive.AddAddendumAsync(addendum, cancellationToken).ConfigureAwait(false);
        LogProofArchived(_logger, item.ProviderReference, link.DocumentId);
        return true;
    }
}
