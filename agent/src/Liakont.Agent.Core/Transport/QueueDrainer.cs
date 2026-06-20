namespace Liakont.Agent.Core.Transport;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Storage;
using Newtonsoft.Json;

/// <summary>
/// Draine la <see cref="LocalQueue"/> vers la plateforme selon l'ACQUITTEMENT EN DEUX TEMPS (ADR-0012)
/// et les codes de réponse F12 §3.3. AUCUNE logique métier : l'agent LIT un statut et applique une
/// règle mécanique (purger / renvoyer / signaler), il n'interprète jamais l'état fiscal (CLAUDE.md n°6).
/// <para>
/// Un cycle <see cref="DrainOnce"/> :
/// </para>
/// <list type="number">
///   <item>RÉCONCILIE chaque document « en cours » via le point de statut : Processed → purge ;
///   Rejected → purge + signalement ; Pending/inconnu → renvoi (remis « en attente »).</item>
///   <item>POUSSE les documents « en attente » par lots (≤ <c>maxBatchSize</c>) ; un accusé
///   (accepted/duplicate) marque l'élément « en cours » — JAMAIS purgé sur le seul push. 413 →
///   re-découpe ; 400 → erreur (pas de retry) ; 429/5xx/réseau → backoff puis arrêt (rien perdu).</item>
///   <item>POUSSE les PDF (liés puis pool) ; succès → purge (un temps, pas de réconciliation).</item>
/// </list>
/// </summary>
public sealed class QueueDrainer
{
    /// <summary>Taille de lot par défaut (limite d'ingestion PIV04, F12 §3.1).</summary>
    public const int DefaultMaxBatchSize = 100;

    /// <summary>Nombre de tentatives transitoires (429/5xx/réseau) par lot avant d'arrêter le cycle.</summary>
    public const int DefaultMaxTransientAttempts = 3;

    private readonly LocalQueue _queue;
    private readonly IPlatformClient _client;
    private readonly IAgentLog _log;
    private readonly ExponentialBackoff _backoff;
    private readonly Action<TimeSpan> _wait;
    private readonly int _maxBatchSize;
    private readonly int _maxTransientAttempts;

    /// <summary>Crée un drainage.</summary>
    /// <param name="queue">File locale source.</param>
    /// <param name="client">Couture de transport vers la plateforme.</param>
    /// <param name="log">Journal de l'agent.</param>
    /// <param name="backoff">Calculateur de backoff (par défaut 2 s → 5 min).</param>
    /// <param name="wait">Fonction d'attente injectable (par défaut <see cref="Thread.Sleep(TimeSpan)"/>) — testable sans attente réelle.</param>
    /// <param name="maxBatchSize">Taille maximale d'un lot de documents.</param>
    /// <param name="maxTransientAttempts">Tentatives transitoires par lot avant arrêt du cycle.</param>
    public QueueDrainer(
        LocalQueue queue,
        IPlatformClient client,
        IAgentLog log,
        ExponentialBackoff? backoff = null,
        Action<TimeSpan>? wait = null,
        int maxBatchSize = DefaultMaxBatchSize,
        int maxTransientAttempts = DefaultMaxTransientAttempts)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "La taille de lot doit être strictement positive.");
        }

        if (maxTransientAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTransientAttempts), "Le nombre de tentatives doit être supérieur ou égal à 1.");
        }

        _backoff = backoff ?? new ExponentialBackoff();
        _wait = wait ?? (delay => Thread.Sleep(delay));
        _maxBatchSize = maxBatchSize;
        _maxTransientAttempts = maxTransientAttempts;
    }

    /// <summary>Exécute un cycle de drainage complet (réconciliation → push documents → push PDF).</summary>
    /// <param name="cancellationToken">Jeton d'arrêt : le drainage s'interrompt proprement, les éléments restent en file.</param>
    /// <returns>Les compteurs et l'issue du cycle.</returns>
    public DrainResult DrainOnce(CancellationToken cancellationToken = default)
    {
        var result = new DrainResult();

        if (ReconcileInProgressDocuments(result, cancellationToken))
        {
            LogResult(result);
            return result;
        }

        IReadOnlyList<SourceTaxRegimeDto> regimes = ReadStashedRegimes();
        ExtractorCapabilitiesDto? capabilities = ReadStashedCapabilities();
        if (PushPendingDocuments(result, regimes, capabilities, cancellationToken)
            || PushPendingPdfs(QueueItemKind.Pdf, result, cancellationToken)
            || PushPendingPdfs(QueueItemKind.PdfPool, result, cancellationToken))
        {
            LogResult(result);
            return result;
        }

        LogResult(result);
        return result;
    }

    private static DocumentPushResultDto? ResolveResult(IReadOnlyList<QueuedItem> batch, IReadOnlyList<DocumentPushResultDto> results, int index)
    {
        // Le contrat garantit l'ordre (F12 §3.4) ; on vérifie quand même la référence, avec repli par clé.
        if (index < results.Count && string.Equals(results[index].SourceReference, batch[index].SourceReference, StringComparison.Ordinal))
        {
            return results[index];
        }

        foreach (DocumentPushResultDto candidate in results)
        {
            if (string.Equals(candidate.SourceReference, batch[index].SourceReference, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    // Phase 1 — réconciliation (ADR-0012). Renvoie true si le drainage doit s'arrêter.
    private bool ReconcileInProgressDocuments(DrainResult result, CancellationToken token)
    {
        foreach (QueuedItem item in _queue.Peek(QueueItemStatus.InProgress, QueueItemKind.Document, int.MaxValue))
        {
            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return true;
            }

            if (string.IsNullOrEmpty(item.PayloadHash))
            {
                continue; // un document porte toujours une empreinte ; sûreté défensive.
            }

            DocumentStatusOutcome outcome = _client.GetDocumentStatus(item.SourceReference, item.PayloadHash!);
            if (outcome.Kind != PlatformResponseKind.Ok)
            {
                // Clé invalide / update / indisponibilité : on arrête, l'élément reste « en cours ».
                result.StoppedBy = outcome.Kind;
                return true;
            }

            ApplyReconciliation(item, outcome, result);
        }

        return false;
    }

    private void ApplyReconciliation(QueuedItem item, DocumentStatusOutcome outcome, DrainResult result)
    {
        if (outcome.Status == DocumentIntakeStatus.Processed)
        {
            _queue.Acknowledge(item.Id);
            result.DocumentsAcknowledged++;
        }
        else if (outcome.Status == DocumentIntakeStatus.Rejected)
        {
            _queue.Acknowledge(item.Id);
            SignalDocumentRejected(item.SourceReference, outcome.Reason);
            result.DocumentsRejected++;
        }
        else
        {
            // Pending ou clé inconnue (null) : reçu mais non rangé → renvoi au prochain push.
            _queue.MarkPending(item.Id);
            result.DocumentsResent++;
        }
    }

    // Phase 2 — push des documents en attente. Renvoie true si le drainage doit s'arrêter.
    private bool PushPendingDocuments(DrainResult result, IReadOnlyList<SourceTaxRegimeDto> regimes, ExtractorCapabilitiesDto? capabilities, CancellationToken token)
    {
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return true;
            }

            IReadOnlyList<QueuedItem> batch = _queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, _maxBatchSize);
            if (batch.Count == 0)
            {
                return false;
            }

            if (PushDocumentBatch(batch, regimes, capabilities, result, token))
            {
                return true;
            }
        }
    }

    // Pousse un lot ; gère le backoff (429/5xx/réseau) et la re-découpe (413). Renvoie true si arrêt.
    private bool PushDocumentBatch(IReadOnlyList<QueuedItem> batch, IReadOnlyList<SourceTaxRegimeDto> regimes, ExtractorCapabilitiesDto? capabilities, DrainResult result, CancellationToken token)
    {
        if (batch.Count == 0)
        {
            return false;
        }

        int attempt = 1;
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return true;
            }

            var canonicalDocuments = batch.Select(b => b.PayloadJson!).ToList();
            PushBatchOutcome outcome = _client.PushDocuments(canonicalDocuments, regimes, capabilities);

            switch (outcome.Kind)
            {
                case PlatformResponseKind.Ok:
                    ApplyPushResults(batch, outcome.Results, result);
                    return false;

                case PlatformResponseKind.BadRequest:
                    MarkBatchErrored(batch, outcome.Reason, result);
                    return false;

                case PlatformResponseKind.Unauthorized:
                case PlatformResponseKind.UpgradeRequired:
                    result.StoppedBy = outcome.Kind;
                    return true;

                case PlatformResponseKind.PayloadTooLarge:
                    return ResplitAndPush(batch, regimes, capabilities, result, token);

                default:
                    // Throttled / TransportError : backoff puis nouvelle tentative, sinon arrêt (rien perdu).
                    if (attempt >= _maxTransientAttempts)
                    {
                        result.StoppedBy = outcome.Kind;
                        return true;
                    }

                    Wait(_backoff.DelayFor(attempt), token);
                    attempt++;
                    break;
            }
        }
    }

    private bool ResplitAndPush(IReadOnlyList<QueuedItem> batch, IReadOnlyList<SourceTaxRegimeDto> regimes, ExtractorCapabilitiesDto? capabilities, DrainResult result, CancellationToken token)
    {
        if (batch.Count == 1)
        {
            QueuedItem single = batch[0];
            _queue.MarkError(single.Id, "Document trop volumineux pour l'ingestion (413).");
            SignalDocumentError(single.SourceReference, "document trop volumineux (413)");
            result.DocumentsErrored++;
            return false;
        }

        int middle = batch.Count / 2;
        var firstHalf = batch.Take(middle).ToList();
        var secondHalf = batch.Skip(middle).ToList();
        if (PushDocumentBatch(firstHalf, regimes, capabilities, result, token))
        {
            return true;
        }

        return PushDocumentBatch(secondHalf, regimes, capabilities, result, token);
    }

    private void ApplyPushResults(IReadOnlyList<QueuedItem> batch, IReadOnlyList<DocumentPushResultDto> results, DrainResult result)
    {
        for (int i = 0; i < batch.Count; i++)
        {
            QueuedItem item = batch[i];
            DocumentPushResultDto? matched = ResolveResult(batch, results, i);

            if (matched != null && matched.Status == DocumentPushStatus.Rejected)
            {
                // Rejet terminal au push : payload non conforme → purge + signalement, jamais re-poussé.
                _queue.Acknowledge(item.Id);
                SignalDocumentRejected(item.SourceReference, matched.Reason);
                result.DocumentsRejected++;
            }
            else
            {
                // Accepted / Duplicate / réponse désalignée : « reçu, en cours » (ADR-0012) — jamais
                // purgé sur le seul push ; la réconciliation par statut tranchera.
                _queue.MarkInProgress(item.Id);
                result.DocumentsInProgress++;
            }
        }
    }

    private void MarkBatchErrored(IReadOnlyList<QueuedItem> batch, string? reason, DrainResult result)
    {
        foreach (QueuedItem item in batch)
        {
            _queue.MarkError(item.Id, reason ?? "Lot rejeté (400) : payload non conforme au contrat.");
            SignalDocumentError(item.SourceReference, reason ?? "payload non conforme (400)");
            result.DocumentsErrored++;
        }
    }

    // Phase 3 — push des PDF d'un type (un fichier à la fois). Renvoie true si arrêt.
    private bool PushPendingPdfs(QueueItemKind kind, DrainResult result, CancellationToken token)
    {
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return true;
            }

            IReadOnlyList<QueuedItem> pending = _queue.Peek(QueueItemStatus.Pending, kind, 1);
            if (pending.Count == 0)
            {
                return false;
            }

            if (PushSinglePdf(kind, pending[0], result, token))
            {
                return true;
            }
        }
    }

    private bool PushSinglePdf(QueueItemKind kind, QueuedItem item, DrainResult result, CancellationToken token)
    {
        int attempt = 1;
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return true;
            }

            PdfPushOutcome outcome = kind == QueueItemKind.Pdf
                ? _client.PushLinkedPdf(item.SourceReference, item.FilePath!)
                : _client.PushPoolPdf(item.FilePath!);

            switch (outcome.Kind)
            {
                case PlatformResponseKind.Ok:
                    _queue.Acknowledge(item.Id);
                    result.PdfsAcknowledged++;
                    return false;

                case PlatformResponseKind.BadRequest:
                case PlatformResponseKind.PayloadTooLarge:
                    _queue.MarkError(item.Id, outcome.Reason ?? "Échec du push du PDF.");
                    SignalPdfError(item.SourceReference, outcome);
                    result.PdfsErrored++;
                    return false;

                case PlatformResponseKind.Unauthorized:
                case PlatformResponseKind.UpgradeRequired:
                    result.StoppedBy = outcome.Kind;
                    return true;

                default:
                    if (attempt >= _maxTransientAttempts)
                    {
                        result.StoppedBy = outcome.Kind;
                        return true;
                    }

                    Wait(_backoff.DelayFor(attempt), token);
                    attempt++;
                    break;
            }
        }
    }

    private IReadOnlyList<SourceTaxRegimeDto> ReadStashedRegimes()
    {
        string? json = _queue.GetState(LocalQueue.SourceTaxRegimesKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SourceTaxRegimeDto>();
        }

        try
        {
            return JsonConvert.DeserializeObject<List<SourceTaxRegimeDto>>(json!)
                ?? (IReadOnlyList<SourceTaxRegimeDto>)Array.Empty<SourceTaxRegimeDto>();
        }
        catch (JsonException)
        {
            return Array.Empty<SourceTaxRegimeDto>();
        }
    }

    private ExtractorCapabilitiesDto? ReadStashedCapabilities()
    {
        string? json = _queue.GetState(LocalQueue.ExtractorCapabilitiesKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<ExtractorCapabilitiesDto>(json!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Wait(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero || token.IsCancellationRequested)
        {
            return;
        }

        _wait(delay);
    }

    private void SignalDocumentRejected(string sourceReference, string? reason) =>
        _log.Warn(
            $"Document {sourceReference} rejeté par la plateforme ({reason ?? "motif non précisé"}) — " +
            "corrigez la donnée source puis ré-extrayez ; il ne sera pas re-poussé en l'état.");

    private void SignalDocumentError(string sourceReference, string reason) =>
        _log.Warn($"Document {sourceReference} en erreur de push ({reason}) — vérifiez le contrat de l'agent ; signalé au heartbeat.");

    private void SignalPdfError(string sourceReference, PdfPushOutcome outcome) =>
        _log.Warn(
            $"PDF {sourceReference} en erreur de push ({outcome.Reason ?? outcome.Kind.ToString()}) — " +
            "vérifiez le fichier source ; signalé au heartbeat.");

    private void LogResult(DrainResult result)
    {
        if (result.StoppedBy == PlatformResponseKind.Unauthorized)
        {
            _log.Error("Push interrompu : clé API invalide ou révoquée (401/403). Diagnostiquez avec « liakont-agent test-api ».");
        }
        else if (result.StoppedBy == PlatformResponseKind.UpgradeRequired)
        {
            _log.Warn("Push interrompu : version d'agent non supportée (426). Une mise à jour est requise (auto-update AGT04).");
        }
        else if (result.StoppedBy.HasValue)
        {
            _log.Warn($"Push interrompu (indisponibilité {result.StoppedBy}) — les éléments restent en file, reprise au prochain cycle.");
        }

        _log.Info(
            $"Drainage : {result.DocumentsInProgress} en cours, {result.DocumentsAcknowledged} acquitté(s), " +
            $"{result.DocumentsResent} renvoyé(s), {result.DocumentsRejected} rejeté(s), {result.DocumentsErrored} en erreur ; " +
            $"PDF {result.PdfsAcknowledged} OK, {result.PdfsErrored} en erreur.");
    }
}
