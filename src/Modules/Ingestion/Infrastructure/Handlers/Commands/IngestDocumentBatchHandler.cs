namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Commands;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Ingestion.Domain;
using Liakont.Modules.Ingestion.Domain.Entities;
using MediatR;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Ingère un lot de documents poussé par un agent authentifié (F12 §3-4, PIV04). Le lot est NON
/// transactionnel : chaque document est évalué et persisté dans SA PROPRE transaction, et un échec
/// (rejet ou doublon) sur un document n'affecte pas les autres. La réponse porte un résultat individuel
/// par document, dans l'ordre de la requête.
/// </summary>
/// <remarks>
/// Anti-doublon et altération (F06) sont décidés par <see cref="DocumentIngestionDecision"/> sur
/// l'empreinte canonique du payload (PIV02), scopée au tenant de l'agent authentifié (jamais le corps —
/// CLAUDE.md n°9). Pour chaque document ACCEPTÉ : création du document en état <c>Detected</c> (port
/// <see cref="IDocumentIntake"/>), inscription au registre de réception et publication des événements
/// d'intégration dans la MÊME transaction (<see cref="DocumentReceivedV1"/> toujours ;
/// <see cref="SourceAlterationDetectedV1"/> en plus si la source a été altérée).
/// </remarks>
public sealed class IngestDocumentBatchHandler : IRequestHandler<IngestDocumentBatchCommand, PushBatchResponseDto>
{
    private const int Version = 1;
    private const string ModuleSource = "ingestion";

    private readonly IReceivedDocumentUnitOfWorkFactory _uowFactory;
    private readonly ISourceTaxRegimeWriter _sourceTaxRegimeWriter;
    private readonly IDocumentIntake _documentIntake;

    public IngestDocumentBatchHandler(
        IReceivedDocumentUnitOfWorkFactory uowFactory,
        ISourceTaxRegimeWriter sourceTaxRegimeWriter,
        IDocumentIntake documentIntake)
    {
        _uowFactory = uowFactory;
        _sourceTaxRegimeWriter = sourceTaxRegimeWriter;
        _documentIntake = documentIntake;
    }

    public async Task<PushBatchResponseDto> Handle(IngestDocumentBatchCommand request, CancellationToken cancellationToken)
    {
        // 1. Métadonnée de push : persister les régimes de TVA source observés (idempotent), pour TVA03.
        await PersistSourceTaxRegimesAsync(request, cancellationToken);

        // 2. Traiter chaque document indépendamment (lot NON transactionnel, résultat individuel).
        var results = new List<DocumentPushResultDto>(request.Documents.Count);
        foreach (var document in request.Documents)
        {
            results.Add(await ProcessDocumentAsync(request, document, cancellationToken));
        }

        return new PushBatchResponseDto(results);
    }

    private async Task PersistSourceTaxRegimesAsync(IngestDocumentBatchCommand request, CancellationToken cancellationToken)
    {
        if (request.SourceTaxRegimes.Count == 0)
        {
            return;
        }

        var observations = new List<SourceTaxRegimeObservation>(request.SourceTaxRegimes.Count);
        foreach (var regime in request.SourceTaxRegimes)
        {
            if (string.IsNullOrWhiteSpace(regime.Code))
            {
                continue;
            }

            observations.Add(new SourceTaxRegimeObservation
            {
                Code = regime.Code,
                Label = regime.Label,
                Occurrences = regime.Occurrences,
            });
        }

        await _sourceTaxRegimeWriter.UpsertAsync(request.TenantId, observations, cancellationToken);
    }

    private async Task<DocumentPushResultDto> ProcessDocumentAsync(
        IngestDocumentBatchCommand request,
        PivotDocumentDto document,
        CancellationToken cancellationToken)
    {
        // Validation de contrat par document : un document malformé est REJETÉ ENTIÈREMENT (jamais
        // d'acceptation partielle, F12 §3-4) sans bloquer le lot. La référence source est la clé de
        // réconciliation/altération ; le numéro (BT-1) est la clé d'idempotence vers la PA.
        var sourceReference = document?.SourceReference;
        if (document is null || string.IsNullOrWhiteSpace(sourceReference))
        {
            return new DocumentPushResultDto(
                sourceReference ?? string.Empty,
                DocumentPushStatus.Rejected,
                "Référence source manquante (champ obligatoire du contrat).");
        }

        if (string.IsNullOrWhiteSpace(document.Number))
        {
            return new DocumentPushResultDto(
                sourceReference,
                DocumentPushStatus.Rejected,
                "Numéro de document manquant (EN 16931 BT-1, champ obligatoire du contrat).");
        }

        string payloadHash;
        try
        {
            payloadHash = PayloadHasher.ComputeHash(document);
        }
        catch (Exception)
        {
            // Payload non sérialisable canoniquement = non conforme au contrat → rejet (jamais 500).
            return new DocumentPushResultDto(
                sourceReference,
                DocumentPushStatus.Rejected,
                "Payload non conforme au contrat (sérialisation canonique impossible).");
        }

        var receivedAt = DateTimeOffset.UtcNow;

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var payloadKnown = await uow.PayloadHashExistsAsync(request.TenantId, payloadHash, cancellationToken);
        var existingHash = await uow.GetLatestHashForSourceReferenceAsync(request.TenantId, sourceReference, cancellationToken);
        var decision = DocumentIngestionDecision.Evaluate(payloadKnown, existingHash, payloadHash);

        if (!decision.IsAccepted)
        {
            // Doublon : aucun effet (idempotence — re-push complet après réinstallation d'agent).
            return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Duplicate);
        }

        // Identifiant attribué par l'ingestion, partagé par le document, la réception et l'événement.
        var documentId = Guid.NewGuid();

        // Création du document en état Detected (port Documents — no-op jusqu'à TRK02). Fail-closed :
        // si la création échoue, rien n'est inscrit ni publié pour ce document.
        await _documentIntake.RegisterDetectedDocumentAsync(
            new DetectedDocumentIntake
            {
                DocumentId = documentId,
                TenantId = request.TenantId,
                SourceReference = sourceReference,
                PayloadHash = payloadHash,
                Document = document,
                ReceivedAtUtc = receivedAt,
            },
            cancellationToken);

        var received = ReceivedDocument.Create(
            request.TenantId,
            sourceReference,
            payloadHash,
            documentId,
            request.ContractVersion,
            receivedAt);

        try
        {
            await uow.InsertReceivedDocumentAsync(received, cancellationToken);
        }
        catch (ConflictException)
        {
            // Course : un lot concurrent a inséré la même empreinte entre l'évaluation et l'insertion.
            // On traite comme doublon (aucun événement publié pour cette tentative).
            return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Duplicate);
        }

        await uow.WriteEventAsync(
            new IntegrationEvent<DocumentReceivedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = IngestionEventTypes.DocumentReceived,
                OccurredAt = receivedAt,
                CorrelationId = documentId,
                ModuleSource = ModuleSource,
                Version = Version,
                Payload = new DocumentReceivedV1
                {
                    TenantId = request.TenantId,
                    DocumentId = documentId,
                    SourceReference = sourceReference,
                    PayloadHash = payloadHash,
                    ReceivedAtUtc = receivedAt,
                },
            },
            cancellationToken);

        if (decision.IsAlteration)
        {
            await uow.WriteEventAsync(
                new IntegrationEvent<SourceAlterationDetectedV1>
                {
                    EventId = Guid.NewGuid(),
                    EventType = IngestionEventTypes.SourceAlterationDetected,
                    OccurredAt = receivedAt,
                    CorrelationId = documentId,
                    ModuleSource = ModuleSource,
                    Version = Version,
                    Payload = new SourceAlterationDetectedV1
                    {
                        TenantId = request.TenantId,
                        SourceReference = sourceReference,
                        PreviousPayloadHash = decision.PreviousPayloadHash!,
                        NewPayloadHash = payloadHash,
                        DocumentId = documentId,
                        DetectedAtUtc = receivedAt,
                    },
                },
                cancellationToken);
        }

        await uow.CommitAsync(cancellationToken);

        return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Accepted);
    }
}
