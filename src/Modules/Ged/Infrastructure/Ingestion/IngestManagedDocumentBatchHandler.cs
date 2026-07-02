namespace Liakont.Modules.Ged.Infrastructure.Ingestion;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Ged;
using Liakont.Agent.Contracts.Ged.Serialization;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Ged.Application.Ingestion;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Contracts.DTOs;
using Liakont.Modules.Ged.Contracts.Events;
using Liakont.Modules.Ged.Domain.Ingestion;
using Liakont.Modules.Staging.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Handler d'ingestion d'un LOT de documents gérés (canal GED, F19 §2.4/§4.3, item GED05b). Pour CHAQUE document :
/// (1) sérialisation canonique GED (<see cref="GedCanonicalJson"/>) + empreinte (<see cref="PayloadHasher"/> réutilisé
/// tel quel) ; (2) décision d'anti-doublon PURE (<see cref="GedIngestionDecision"/>, RE-COPIÉE dans le module GED,
/// RL-01) sur le registre GED DÉDIÉ (espace de hash séparé du canal fiscal) ; (3) si accepté : staging du pivot AVANT
/// le commit (ADR-0014) puis, DANS UNE MÊME TRANSACTION EN BASE SYSTÈME, INSERT du registre + écriture de
/// <see cref="ManagedDocumentReceivedV1"/> dans l'outbox (atomicité registre + événement, RL-03). AUCUN Document
/// fiscal n'est créé (le canal GED n'appelle JAMAIS <c>IDocumentIntake</c>). Un document invalide/en doublon ne fait
/// JAMAIS échouer le lot (résultat individuel). Tenant-scopé par la clé API de l'agent (n°9).
/// </summary>
internal sealed partial class IngestManagedDocumentBatchHandler
    : IRequestHandler<IngestManagedDocumentBatchCommand, ManagedDocumentBatchResultDto>
{
    private readonly IGedReceivedDocumentUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IPayloadStagingStore _stagingStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestManagedDocumentBatchHandler> _logger;

    public IngestManagedDocumentBatchHandler(
        IGedReceivedDocumentUnitOfWorkFactory unitOfWorkFactory,
        IPayloadStagingStore stagingStore,
        ILogger<IngestManagedDocumentBatchHandler> logger)
        : this(unitOfWorkFactory, stagingStore, logger, TimeProvider.System)
    {
    }

    internal IngestManagedDocumentBatchHandler(
        IGedReceivedDocumentUnitOfWorkFactory unitOfWorkFactory,
        IPayloadStagingStore stagingStore,
        ILogger<IngestManagedDocumentBatchHandler> logger,
        TimeProvider timeProvider)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _stagingStore = stagingStore;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<ManagedDocumentBatchResultDto> Handle(IngestManagedDocumentBatchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<ManagedDocumentPushResultDto>(request.Documents.Count);
        foreach (var document in request.Documents)
        {
            results.Add(await ProcessDocumentAsync(request.TenantId, document, cancellationToken));
        }

        return new ManagedDocumentBatchResultDto(results);
    }

    [LoggerMessage(EventId = 7300, Level = LogLevel.Warning,
        Message = "Ingestion GED : document « {SourceReference} » rejeté (sérialisation canonique impossible) — jamais envoyé faux (n°3).")]
    private static partial void LogSerializationRejected(ILogger logger, string sourceReference, Exception exception);

    private async Task<ManagedDocumentPushResultDto> ProcessDocumentAsync(
        string tenantId,
        IngestedDocumentDto document,
        CancellationToken cancellationToken)
    {
        // Corps validé à la frontière : un champ obligatoire absent est rejeté PAR DOCUMENT, jamais propagé en
        // violation NOT NULL (500) à l'insert du registre.
        if (string.IsNullOrWhiteSpace(document.SourceReference))
        {
            return new ManagedDocumentPushResultDto(document.SourceReference ?? string.Empty, ManagedDocumentPushStatus.Rejected);
        }

        if (string.IsNullOrWhiteSpace(document.DocumentType))
        {
            return new ManagedDocumentPushResultDto(document.SourceReference, ManagedDocumentPushStatus.Rejected);
        }

        // Sérialisation canonique GED + empreinte. Une garde du writer (ex. collision de clé SourceFields après NFC,
        // RL-39) LÈVE : on rejette CE document — « bloquer plutôt qu'envoyer faux » (n°3), jamais un 500 sur le lot.
        string canonicalJson;
        try
        {
            canonicalJson = GedCanonicalJson.Serialize(document);
        }
        catch (ArgumentException ex)
        {
            LogSerializationRejected(_logger, document.SourceReference, ex);
            return new ManagedDocumentPushResultDto(document.SourceReference, ManagedDocumentPushStatus.Rejected);
        }

        var payloadHash = PayloadHasher.ComputeHash(canonicalJson);
        var managedDocumentId = Guid.NewGuid();
        var receivedAt = _timeProvider.GetUtcNow();

        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        var payloadKnown = await unitOfWork.PayloadHashExistsAsync(tenantId, payloadHash, cancellationToken);
        var existingHash = await unitOfWork.GetLatestHashForSourceReferenceAsync(tenantId, document.SourceReference, cancellationToken);
        var decision = GedIngestionDecision.Evaluate(payloadKnown, existingHash, payloadHash);

        if (!decision.IsAccepted)
        {
            // Doublon strict : aucune réécriture, aucun événement (idempotent).
            return new ManagedDocumentPushResultDto(document.SourceReference, ManagedDocumentPushStatus.Duplicate);
        }

        var registryEntry = GedReceivedDocument.Create(
            tenantId,
            document.SourceReference,
            payloadHash,
            managedDocumentId,
            GedContractVersion.Current,
            receivedAt);

        try
        {
            await unitOfWork.InsertReceivedDocumentAsync(registryEntry, cancellationToken);
        }
        catch (ConflictException)
        {
            // Course entre lots concurrents : l'empreinte vient d'être insérée par un autre push GED → doublon.
            return new ManagedDocumentPushResultDto(document.SourceReference, ManagedDocumentPushStatus.Duplicate);
        }

        await unitOfWork.WriteEventAsync(
            new IntegrationEvent<ManagedDocumentReceivedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = GedEventTypes.ManagedDocumentReceived,
                OccurredAt = receivedAt,
                CorrelationId = managedDocumentId,
                ModuleSource = "ged",
                Version = 1,
                Payload = new ManagedDocumentReceivedV1
                {
                    TenantId = tenantId,
                    ManagedDocumentId = managedDocumentId,
                    SourceReference = document.SourceReference,
                    PayloadHash = payloadHash,
                    ReceivedAtUtc = receivedAt,
                },
            },
            cancellationToken);

        // Staging du pivot GED durci AVANT le commit (invariant d'ordre ADR-0014) : quand l'événement est drainé, le
        // contenu relu par le consommateur est GARANTI présent. Idempotent sur la clé (tenant, id, hash). Un échec de
        // staging LÈVE avant le commit → toute la transaction est annulée (rien de committé), jamais un état partiel.
        await _stagingStore.WriteAsync(
            new StagedPayloadKey(tenantId, managedDocumentId, payloadHash),
            canonicalJson,
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new ManagedDocumentPushResultDto(document.SourceReference, ManagedDocumentPushStatus.Accepted);
    }
}
