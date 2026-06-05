namespace Liakont.Agent.Core.Extraction;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Storage;
using Newtonsoft.Json;

/// <summary>
/// Un cycle d'extraction (F12 §2.2 — EXTRACT → COLLECT → enqueue). En LECTURE SEULE de la source, il
/// transforme les documents pivot en éléments de la <see cref="LocalQueue"/> (clé idempotente
/// <c>(source_reference, payload_hash)</c>), collecte les PDF selon les capacités déclarées, restitue
/// les régimes de TVA source pour le prochain push, et avance le filigrane d'extraction. AUCUNE logique
/// métier : pas de mapping TVA, pas de validation (CLAUDE.md n°6) — il transporte.
/// </summary>
public sealed class ExtractionCycle
{
    private readonly LocalQueue _queue;
    private readonly IAgentLog _log;

    /// <summary>Crée un cycle d'extraction.</summary>
    /// <param name="queue">File locale destinataire des éléments à pousser.</param>
    /// <param name="log">Journal de l'agent.</param>
    public ExtractionCycle(LocalQueue queue, IAgentLog log)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Exécute un cycle sur la période [<paramref name="fromInclusiveUtc"/>, <paramref name="toExclusiveUtc"/>[.
    /// Peut lever <see cref="SourceUnavailableException"/> (réessayable) ou
    /// <see cref="SourceSchemaException"/> (fatale) propagée par l'extracteur.
    /// </summary>
    /// <param name="extractor">Extracteur source (plug-in du lot ADP / fixtures).</param>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les compteurs du cycle.</returns>
    public ExtractionResult Run(IExtractor extractor, DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        if (extractor is null)
        {
            throw new ArgumentNullException(nameof(extractor));
        }

        if (toExclusiveUtc < fromInclusiveUtc)
        {
            throw new ArgumentException("La borne haute de la période doit être postérieure ou égale à la borne basse.", nameof(toExclusiveUtc));
        }

        ExtractorCapabilities capabilities = extractor.Capabilities;
        int documentsEnqueued = 0;
        int documentsSkipped = 0;
        int linkedPdfs = 0;

        foreach (PivotDocumentDto document in extractor.ExtractDocuments(fromInclusiveUtc, toExclusiveUtc))
        {
            string canonicalJson = CanonicalJson.Serialize(document);
            string hash = PayloadHasher.ComputeHash(canonicalJson);

            // Anti re-push (F12 §2.2) : un document déjà acquitté (même hash) n'est pas ré-enfilé.
            if (_queue.IsAlreadyPushed(QueueItemKind.Document, document.SourceReference, hash))
            {
                documentsSkipped++;
                continue;
            }

            if (_queue.Enqueue(QueueItem.ForDocument(document.SourceReference, hash, canonicalJson)) == EnqueueResult.Enqueued)
            {
                documentsEnqueued++;
            }

            if (capabilities.ProvidesSourceDocuments)
            {
                linkedPdfs += CollectLinkedPdfs(extractor, document.SourceReference);
            }
        }

        int poolPdfs = capabilities.ProvidesUnlinkedDocumentPool
            ? CollectPoolPdfs(extractor, fromInclusiveUtc, toExclusiveUtc)
            : 0;

        int regimes = StashSourceTaxRegimes(extractor);

        _queue.SetExtractionWatermarkUtc(toExclusiveUtc);

        _log.Info(
            $"Run d'extraction terminé : {documentsEnqueued} document(s) enfilé(s), {documentsSkipped} ignoré(s), " +
            $"{linkedPdfs} PDF lié(s), {poolPdfs} PDF de pool, {regimes} régime(s) TVA source.");

        return new ExtractionResult(documentsEnqueued, documentsSkipped, linkedPdfs, poolPdfs, regimes);
    }

    private int CollectLinkedPdfs(IExtractor extractor, string sourceReference)
    {
        int enqueued = 0;
        foreach (SourceAttachment attachment in extractor.GetAttachments(sourceReference))
        {
            // Discriminant stable par fichier (nom) : permet plusieurs pièces jointes par document
            // tout en gardant l'anti re-push (Acknowledge enregistre la clé non nulle).
            if (_queue.IsAlreadyPushed(QueueItemKind.Pdf, attachment.SourceReference, attachment.FileName))
            {
                continue;
            }

            if (_queue.Enqueue(QueueItem.ForPdf(QueueItemKind.Pdf, attachment.SourceReference, attachment.FilePath, attachment.FileName)) == EnqueueResult.Enqueued)
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    private int CollectPoolPdfs(IExtractor extractor, DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        int enqueued = 0;
        foreach (PoolDocument poolDocument in extractor.ListPoolDocuments(fromInclusiveUtc, toExclusiveUtc))
        {
            if (_queue.IsAlreadyPushed(QueueItemKind.PdfPool, poolDocument.PoolReference, poolDocument.PoolReference))
            {
                continue;
            }

            if (_queue.Enqueue(QueueItem.ForPdf(QueueItemKind.PdfPool, poolDocument.PoolReference, poolDocument.FilePath, poolDocument.PoolReference)) == EnqueueResult.Enqueued)
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    private int StashSourceTaxRegimes(IExtractor extractor)
    {
        IReadOnlyList<SourceTaxRegimeDto> regimes = extractor.ListSourceTaxRegimes();
        if (regimes is null || regimes.Count == 0)
        {
            _queue.SetState(LocalQueue.SourceTaxRegimesKey, null);
            return 0;
        }

        // Métadonnée de push (TVA03) : jointe au prochain lot par le drainage. Régimes BRUTS, jamais
        // interprétés par l'agent (CLAUDE.md n°2).
        _queue.SetState(LocalQueue.SourceTaxRegimesKey, JsonConvert.SerializeObject(regimes));
        return regimes.Count;
    }
}
