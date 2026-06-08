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
using Stratum.Common.Abstractions.Exceptions;
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
                    // CONCURRENCE : le verrou advisory (pg_advisory_lock) sérialise les passes concurrentes sur
                    // ce PDF — aucune double écriture possible (la 2e passe voit l'entrée de file et saute).
                    // CRASH mono-processus ENTRE le fait d'audit et l'AddAsync : la repasse ultérieure ne trouve
                    // pas d'entrée de file et re-traite le PDF — elle peut alors ré-ajouter un addendum chaîné ET
                    // un second DocumentReconciledAuto. Accepté V1 : append-only + chaîne intacte (aucune
                    // corruption, un auditeur lit deux faits de rapprochement pour le même PDF). L'idempotence
                    // stricte au crash (marqueur de file « en cours » écrit AVANT les effets) est un durcissement
                    // ultérieur, hors périmètre V1.
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
            ?? throw new NotFoundException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        // Sérialise la confirmation manuelle sous verrou advisory pour éviter un double effet si deux
        // requêtes concurrentes confirment le même PDF. Re-lecture de l'entrée sous verrou pour détecter
        // une course gagnée par une autre passe.
        await using IAsyncDisposable processingLock = await _queue.AcquireProcessingLockAsync(entry.PoolPdfId, cancellationToken);
        entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new NotFoundException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        DocumentDto document = await _documentQueries.GetByIdAsync(documentId, cancellationToken)
            ?? throw new NotFoundException($"Document {documentId} introuvable : rapprochement manuel impossible.");

        if (!string.Equals(document.State, IssuedState, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"Le document {document.DocumentNumber} n'est pas émis (état {document.State}) : seul un document émis peut recevoir un PDF réconcilié (TRK07).");
        }

        string reason = $"Rapprochement manuel du PDF « {entry.FileName} » vers le document {document.DocumentNumber} par l'opérateur {operatorIdentity}.";

        // Valide AVANT tout effet (lève si l'entrée est déjà rapprochée — course gagnée par une autre passe
        // sous verrou) ; la mutation n'est persistée qu'après l'addendum WORM et le fait d'audit.
        try
        {
            entry.ConfirmManually(documentId, operatorIdentity, reason, _timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }

        byte[] bytes = await ReadPoolPdfBytesAsync(tenant, entry.PoolPdfId, cancellationToken);
        await AddArchiveAddendumAsync(documentId, document.DocumentNumber, document.IssueDate, entry.FileName, bytes, cancellationToken);
        await _journal.RecordManualReconciliationAsync(documentId, reason, operatorIdentity, cancellationToken);
        await _queue.UpdateAsync(entry, cancellationToken);
    }

    public async Task ConfirmProposalAsync(Guid queueEntryId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire (TRK07).", nameof(operatorIdentity));
        }

        RequireTenant();

        ReconciliationQueueEntry entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new NotFoundException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        if (entry.Status != ReconciliationStatus.PendingManual || entry.ProposedDocumentId is not { } proposedDocumentId)
        {
            throw new ConflictException(
                $"Le PDF « {entry.FileName} » n'est pas une proposition en attente (état {entry.Status}) : seule une proposition de confiance moyenne peut être confirmée (API04/TRK07).");
        }

        // Confiance MOYENNE confirmée par l'opérateur → rapprochement manuel vers le document PROPOSÉ
        // (le serveur fait foi sur la cible). La confirmation manuelle (verrou, addendum WORM, audit) reste
        // l'unique chemin d'effet : on n'en duplique rien ici.
        await ConfirmManualReconciliationAsync(queueEntryId, proposedDocumentId, operatorIdentity, cancellationToken);
    }

    public async Task RejectProposalAsync(Guid queueEntryId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire (API04/TRK07).", nameof(operatorIdentity));
        }

        RequireTenant();

        ReconciliationQueueEntry entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new NotFoundException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        // Sérialise sous verrou advisory (cohérent avec la confirmation) et re-lit l'entrée pour détecter une
        // course gagnée par une autre opération (ex. une confirmation concurrente sur le même PDF).
        await using IAsyncDisposable processingLock = await _queue.AcquireProcessingLockAsync(entry.PoolPdfId, cancellationToken);
        entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new NotFoundException($"Entrée de réconciliation {queueEntryId} introuvable dans ce tenant.");

        try
        {
            // Aucun addendum WORM, aucun fait d'audit document (rien n'est rapproché) : la proposition
            // redevient un orphelin, avec l'identité de l'opérateur conservée sur l'entrée (audit opérationnel).
            entry.RejectProposal(operatorIdentity, _timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            // État incompatible (déjà rapproché, orphelin) : conflit métier → 409 (et non 500).
            throw new ConflictException(ex.Message, ex);
        }

        await _queue.UpdateAsync(entry, cancellationToken);
    }

    public async Task<ReconciliationPdfContent?> OpenQueueEntryPdfAsync(Guid queueEntryId, CancellationToken cancellationToken = default)
    {
        string tenant = RequireTenant();

        ReconciliationQueueEntry? entry = await _queue.GetByIdAsync(queueEntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        // Flux en lecture seule du PDF du pool (anti path-traversal côté store). L'appelant DISPOSE le flux.
        Stream stream = await _pdfStore.OpenPooledPdfAsync(tenant, entry.PoolPdfId, cancellationToken);
        return new ReconciliationPdfContent(stream, entry.FileName);
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

        // Déduplication par DocumentId : la pagination par offset porte sur une clé mutable
        // (last_update_utc) — un même document peut revenir sur deux pages (même garde que
        // LoadIssuedCandidatesAsync, pour que les deux consommateurs soient cohérents).
        var seen = new HashSet<Guid>();
        var result = new List<DocumentWithoutPdfDto>();
        int page = 1;
        while (true)
        {
            IReadOnlyList<DocumentSummaryDto> batch = await _documentQueries.GetByStateAsync(IssuedState, page, IssuedPageSize, cancellationToken);
            foreach (DocumentSummaryDto document in batch)
            {
                if (!reconciled.Contains(document.Id) && seen.Add(document.Id))
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
