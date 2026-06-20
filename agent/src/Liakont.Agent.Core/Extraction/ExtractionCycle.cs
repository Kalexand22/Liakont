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

        // ExtractPayments fait partie du contrat IExtractor, mais le contrat d'ingestion v1
        // (F12 §3.4) n'a AUCUN endpoint de paiement autonome : les encaissements voyagent dans
        // PivotDocumentDto.Payments (poussés avec le document). Le transport des paiements AUTONOMES
        // (e-reporting de paiement F09) est donc DIFFÉRÉ à l'item qui définira ce chemin côté
        // plateforme — en inventer un ici violerait CLAUDE.md n°2 (aucune règle/contrat inventé).
        int poolPdfs = capabilities.ProvidesUnlinkedDocumentPool
            ? CollectPoolPdfs(extractor, fromInclusiveUtc, toExclusiveUtc)
            : 0;

        int regimes = StashSourceTaxRegimes(extractor);

        // Capacités déclarées de la source (ADR-0004 D2 / RD401) : jointes au prochain push comme
        // métadonnée d'enveloppe (non hashée). Déclaratives et stables ; l'agent ne les interprète jamais.
        StashExtractorCapabilities(capabilities);

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
            // Discriminant = chemin du fichier (UNIQUE par fichier physique) : deux pièces jointes de
            // même nom pour un document ne collisionnent pas sur l'index unique de push_queue, tout en
            // gardant l'anti re-push (même chemin = même clé ; Acknowledge enregistre la clé non nulle).
            if (_queue.IsAlreadyPushed(QueueItemKind.Pdf, attachment.SourceReference, attachment.FilePath))
            {
                continue;
            }

            if (_queue.Enqueue(QueueItem.ForPdf(QueueItemKind.Pdf, attachment.SourceReference, attachment.FilePath, attachment.FilePath)) == EnqueueResult.Enqueued)
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
        IReadOnlyList<SourceTaxRegimeDto> regimes;
        try
        {
            regimes = extractor.ListSourceTaxRegimes();
        }
        catch (SourceUnavailableException ex)
        {
            // Le catalogue des régimes (métadonnée TVA03) est BEST-EFFORT : une indisponibilité PASSAGÈRE
            // pendant son rafraîchissement NE DOIT PAS bloquer l'avancée du filigrane ni le flux de documents
            // (déjà extraits ce cycle). On conserve le dernier état connu (pas d'écrasement) et on réessaiera
            // au prochain cycle. Une SourceSchemaException (fatale, intervention requise) reste, elle, propagée.
            _log.Warn($"Régimes TVA source non rafraîchis ce cycle (source momentanément indisponible) : {ex.Message}");
            return 0;
        }

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

    private void StashExtractorCapabilities(ExtractorCapabilities capabilities)
    {
        // Métadonnée de push (RD401) : jointe au prochain lot par le drainage. Capacités DÉCLARÉES,
        // jamais interprétées par l'agent (CLAUDE.md n°6) — les formes énumérées voyagent en valeur brute.
        ExtractorCapabilitiesDto dto = ExtractorCapabilitiesMapper.ToDto(capabilities);
        _queue.SetState(LocalQueue.ExtractorCapabilitiesKey, JsonConvert.SerializeObject(dto));
    }
}
