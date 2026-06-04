namespace Liakont.Modules.Reconciliation.Application;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Contracts.Reconciliation;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Liakont.Modules.Reconciliation.Domain;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Orchestrateur du rapprochement PDF ↔ documents émis (item TRK07). Énumère le pool de PDF non
/// liés du tenant, applique le moteur PUR <see cref="ReconciliationEngine"/>, puis matérialise les effets :
/// pour une correspondance de CONFIANCE HAUTE, ajoute le PDF au paquet d'archive en addendum (WORM, TRK05)
/// et inscrit un fait d'audit append-only (Documents), avant de tracer l'état dans la file d'attente. Une
/// confiance moyenne devient une PROPOSITION, le reste un ORPHELIN. Aucun lien automatique sous la
/// confiance haute (décision 2026-06-02 — un rapprochement erroné en WORM est incorrigible).
/// TENANT-SCOPÉ : la base, le coffre et le pool routent vers le tenant courant (blueprint §7).
/// </summary>
public sealed class ReconciliationService : IReconciliationService, IReconciliationQueries
{
    private const int IssuedPageSize = 200;
    private const string IssuedState = "Issued";
    private const string ReconciledPdfKind = "pdf-reconcilie";

    private readonly IIngestedPdfStore _pdfStore;
    private readonly IPdfTextExtractor _textExtractor;
    private readonly IDocumentQueries _documentQueries;
    private readonly IArchiveService _archiveService;
    private readonly IDocumentReconciliationJournal _journal;
    private readonly IReconciliationQueueStore _queue;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public ReconciliationService(
        IIngestedPdfStore pdfStore,
        IPdfTextExtractor textExtractor,
        IDocumentQueries documentQueries,
        IArchiveService archiveService,
        IDocumentReconciliationJournal journal,
        IReconciliationQueueStore queue,
        ITenantContext tenantContext)
        : this(pdfStore, textExtractor, documentQueries, archiveService, journal, queue, tenantContext, TimeProvider.System)
    {
    }

    internal ReconciliationService(
        IIngestedPdfStore pdfStore,
        IPdfTextExtractor textExtractor,
        IDocumentQueries documentQueries,
        IArchiveService archiveService,
        IDocumentReconciliationJournal journal,
        IReconciliationQueueStore queue,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _pdfStore = pdfStore;
        _textExtractor = textExtractor;
        _documentQueries = documentQueries;
        _archiveService = archiveService;
        _journal = journal;
        _queue = queue;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<ReconciliationRunResult> RunForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        string tenant = RequireTenant();

        IReadOnlyList<PooledPdfReference> pooled = await _pdfStore.ListPooledPdfsAsync(tenant, cancellationToken);
        if (pooled.Count == 0)
        {
            return new ReconciliationRunResult(0, 0, 0, 0);
        }

        IReadOnlyList<DocumentCandidate> candidates = await LoadIssuedCandidatesAsync(cancellationToken);
        Dictionary<Guid, DocumentCandidate> candidatesById = candidates.ToDictionary(c => c.DocumentId);

        int processed = 0, autoLinked = 0, proposed = 0, orphans = 0;
        foreach (PooledPdfReference pdf in pooled)
        {
            await using IAsyncDisposable processingLock = await _queue.AcquireProcessingLockAsync(pdf.PoolPdfId, cancellationToken);

            // Re-vérifie SOUS LE VERROU : une passe concurrente a pu traiter ce PDF entre-temps.
            if (await _queue.FindByPoolPdfIdAsync(pdf.PoolPdfId, cancellationToken) is not null)
            {
                continue;
            }

            byte[] bytes = await ReadPoolPdfBytesAsync(tenant, pdf.PoolPdfId, cancellationToken);
            string? text = _textExtractor.TryExtractText(bytes);
            ReconciliationDecision decision = ReconciliationEngine.Decide(
                new PooledPdfContent(pdf.PoolPdfId, pdf.FileName, text), candidates);

            switch (decision.Outcome)
            {
                case ReconciliationOutcome.AutoLinked:
                    DocumentCandidate matched = candidatesById[decision.MatchedDocumentId!.Value];

                    // Ordre : addendum WORM (preuve) → fait d'audit append-only → marqueur de file d'attente.
                    // Le verrou advisory (pg_advisory_lock) sérialise les passes concurrentes sur ce PDF :
                    // aucune double écriture d'addendum ou de fait d'audit possible. Un crash ENTRE l'addendum et
                    // l'AddAsync (mono-processus, repasse ultérieure) peut au pire ré-ajouter un addendum chaîné
                    // (accepté V1 : append-only + chaîne intacte, aucune corruption). Ce cas reste distingué du
                    // cas concurrent, maintenant sérialisé par le verrou.
                    await AddArchiveAddendumAsync(matched.DocumentId, matched.DocumentNumber, matched.IssueDate, pdf.FileName, bytes, cancellationToken);
                    await _journal.RecordAutomaticReconciliationAsync(matched.DocumentId, decision.Reason, cancellationToken);
                    await _queue.AddAsync(
                        ReconciliationQueueEntry.AutoReconciled(pdf.PoolPdfId, pdf.FileName, matched.DocumentId, decision.Strategy!.Value, decision.Reason, _timeProvider.GetUtcNow()),
                        cancellationToken);
                    autoLinked++;
                    break;

                case ReconciliationOutcome.ProposeManual:
                    await _queue.AddAsync(
                        ReconciliationQueueEntry.PendingProposal(pdf.PoolPdfId, pdf.FileName, decision.MatchedDocumentId!.Value, decision.Reason, _timeProvider.GetUtcNow()),
                        cancellationToken);
                    proposed++;
                    break;

                default:
                    await _queue.AddAsync(
                        ReconciliationQueueEntry.Orphan(pdf.PoolPdfId, pdf.FileName, decision.Reason, _timeProvider.GetUtcNow()),
                        cancellationToken);
                    orphans++;
                    break;
            }

            processed++;
        }

        return new ReconciliationRunResult(processed, autoLinked, proposed, orphans);
    }

    public async Task ConfirmManualReconciliationAsync(
        Guid queueEntryId,
        Guid documentId,
        string operatorIdentity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire (TRK07).", nameof(operatorIdentity));
        }

        string tenant = RequireTenant();

        ReconciliationQueueEntry entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        // Sérialise la confirmation manuelle sous verrou advisory pour éviter un double effet si deux
        // requêtes concurrentes confirment le même PDF. Re-lecture de l'entrée sous verrou pour détecter
        // une course gagnée par une autre passe.
        await using IAsyncDisposable processingLock = await _queue.AcquireProcessingLockAsync(entry.PoolPdfId, cancellationToken);
        entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        DocumentDto document = await _documentQueries.GetByIdAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} introuvable : rapprochement manuel impossible.");

        if (!string.Equals(document.State, IssuedState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Le document {document.DocumentNumber} n'est pas émis (état {document.State}) : seul un document émis peut recevoir un PDF réconcilié (TRK07).");
        }

        string reason = $"Rapprochement manuel du PDF « {entry.FileName} » vers le document {document.DocumentNumber} par l'opérateur {operatorIdentity}.";

        // Valide AVANT tout effet (lève si l'entrée est déjà rapprochée — course gagnée par une autre passe
        // sous verrou) ; la mutation n'est persistée qu'après l'addendum WORM et le fait d'audit.
        entry.ConfirmManually(documentId, operatorIdentity, reason, _timeProvider.GetUtcNow());

        byte[] bytes = await ReadPoolPdfBytesAsync(tenant, entry.PoolPdfId, cancellationToken);
        await AddArchiveAddendumAsync(documentId, document.DocumentNumber, document.IssueDate, entry.FileName, bytes, cancellationToken);
        await _journal.RecordManualReconciliationAsync(documentId, reason, operatorIdentity, cancellationToken);
        await _queue.UpdateAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationProposalDto>> GetPendingProposalsAsync(CancellationToken cancellationToken = default)
    {
        RequireTenant();
        IReadOnlyList<ReconciliationQueueEntry> entries = await _queue.ListByStatusAsync(ReconciliationStatus.PendingManual, cancellationToken);
        return entries.Select(e => new ReconciliationProposalDto(
            e.Id,
            e.PoolPdfId,
            e.FileName,
            e.ProposedDocumentId!.Value,
            (e.Strategy ?? MatchStrategy.DateAndAmount).ToString(),
            (e.Confidence ?? MatchConfidence.Medium).ToString(),
            e.Detail,
            e.CreatedUtc)).ToList();
    }

    public async Task<IReadOnlyList<OrphanPdfDto>> GetOrphanPdfsAsync(CancellationToken cancellationToken = default)
    {
        RequireTenant();
        IReadOnlyList<ReconciliationQueueEntry> entries = await _queue.ListByStatusAsync(ReconciliationStatus.Orphan, cancellationToken);
        return entries.Select(e => new OrphanPdfDto(e.Id, e.PoolPdfId, e.FileName, e.Detail, e.CreatedUtc)).ToList();
    }

    public async Task<IReadOnlyList<DocumentWithoutPdfDto>> GetIssuedDocumentsWithoutPdfAsync(CancellationToken cancellationToken = default)
    {
        RequireTenant();

        var reconciled = new HashSet<Guid>(await _queue.ListReconciledDocumentIdsAsync(cancellationToken));
        var result = new List<DocumentWithoutPdfDto>();
        int page = 1;
        while (true)
        {
            IReadOnlyList<DocumentSummaryDto> batch = await _documentQueries.GetByStateAsync(IssuedState, page, IssuedPageSize, cancellationToken);
            foreach (DocumentSummaryDto document in batch)
            {
                if (!reconciled.Contains(document.Id))
                {
                    result.Add(new DocumentWithoutPdfDto(document.Id, document.DocumentNumber, document.IssueDate, document.TotalGross));
                }
            }

            if (batch.Count < IssuedPageSize)
            {
                break;
            }

            page++;
        }

        return result;
    }

    private async Task<IReadOnlyList<DocumentCandidate>> LoadIssuedCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<DocumentCandidate>();
        var seen = new HashSet<Guid>();
        int page = 1;
        while (true)
        {
            IReadOnlyList<DocumentSummaryDto> batch = await _documentQueries.GetByStateAsync(IssuedState, page, IssuedPageSize, cancellationToken);
            foreach (DocumentSummaryDto document in batch)
            {
                if (seen.Add(document.Id))
                {
                    candidates.Add(new DocumentCandidate(document.Id, document.DocumentNumber, document.IssueDate, document.TotalGross));
                }
            }

            if (batch.Count < IssuedPageSize)
            {
                break;
            }

            page++;
        }

        return candidates;
    }

    private async Task AddArchiveAddendumAsync(
        Guid documentId,
        string documentNumber,
        DateOnly issueDate,
        string fileName,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await _archiveService.AddAddendumAsync(
            new ArchiveAddendumRequest
            {
                DocumentId = documentId,
                DocumentNumber = documentNumber,
                IssueDate = issueDate,
                Kind = ReconciledPdfKind,
                Attachment = new ArchiveAttachment(fileName, "application/pdf", bytes),
            },
            cancellationToken);
    }

    private async Task<byte[]> ReadPoolPdfBytesAsync(string tenant, string poolPdfId, CancellationToken cancellationToken)
    {
        await using Stream stream = await _pdfStore.OpenPooledPdfAsync(tenant, poolPdfId, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private string RequireTenant()
    {
        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "Le module Reconciliation est tenant-scopé : aucun tenant résolu pour cette opération (blueprint §7).");
        }

        return _tenantContext.TenantId;
    }
}
