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
using Microsoft.Extensions.Logging;
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
/// CLAUDE.md n°9). Pour chaque document ACCEPTÉ : inscription au registre de réception et publication
/// des événements d'intégration dans la MÊME transaction (<see cref="DocumentReceivedV1"/> toujours ;
/// <see cref="SourceAlterationDetectedV1"/> en plus si la source a été altérée), PUIS création du
/// document en état <c>Detected</c> (port <see cref="IDocumentIntake"/>, best-effort post-commit — le
/// déclencheur DURABLE du pipeline reste l'événement outbox, transactionnel avec l'inscription).
/// La métadonnée secondaire (régimes source) est persistée APRÈS la boucle, en best-effort : un échec
/// sur cette métadonnée ne doit jamais faire échouer l'ingestion primaire des documents.
/// </remarks>
public sealed partial class IngestDocumentBatchHandler : IRequestHandler<IngestDocumentBatchCommand, PushBatchResponseDto>
{
    private const int Version = 1;
    private const string ModuleSource = "ingestion";

    private readonly IReceivedDocumentUnitOfWorkFactory _uowFactory;
    private readonly ISourceTaxRegimeWriter _sourceTaxRegimeWriter;
    private readonly IDocumentIntake _documentIntake;
    private readonly ILogger<IngestDocumentBatchHandler> _logger;

    public IngestDocumentBatchHandler(
        IReceivedDocumentUnitOfWorkFactory uowFactory,
        ISourceTaxRegimeWriter sourceTaxRegimeWriter,
        IDocumentIntake documentIntake,
        ILogger<IngestDocumentBatchHandler> logger)
    {
        _uowFactory = uowFactory;
        _sourceTaxRegimeWriter = sourceTaxRegimeWriter;
        _documentIntake = documentIntake;
        _logger = logger;
    }

    public async Task<PushBatchResponseDto> Handle(IngestDocumentBatchCommand request, CancellationToken cancellationToken)
    {
        // 1. Chemin PRIMAIRE : traiter chaque document indépendamment (lot NON transactionnel, résultat individuel).
        var results = new List<DocumentPushResultDto>(request.Documents.Count);
        foreach (var document in request.Documents)
        {
            results.Add(await ProcessDocumentAsync(request, document, cancellationToken));
        }

        // 2. Métadonnée SECONDAIRE (régimes source pour TVA03), best-effort : son échec ne casse pas
        //    l'ingestion primaire déjà committée document par document.
        await PersistSourceTaxRegimesBestEffortAsync(request, cancellationToken);

        return new PushBatchResponseDto(results);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persistance des régimes de TVA source échouée pour le tenant {TenantId} (métadonnée best-effort, ingestion des documents non affectée)")]
    private static partial void LogSourceTaxRegimePersistFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Création du document Detected (port IDocumentIntake) échouée pour {DocumentId} (best-effort ; le déclencheur durable reste l'événement DocumentReceived déjà publié)")]
    private static partial void LogDocumentIntakeFailed(ILogger logger, Guid documentId, Exception exception);

    private async Task PersistSourceTaxRegimesBestEffortAsync(IngestDocumentBatchCommand request, CancellationToken cancellationToken)
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

        if (observations.Count == 0)
        {
            return;
        }

        try
        {
            await _sourceTaxRegimeWriter.UpsertAsync(request.TenantId, observations, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceTaxRegimePersistFailed(_logger, request.TenantId, ex);
        }
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

        // Identifiant attribué par l'ingestion, partagé par le document, la réception et l'événement.
        var documentId = Guid.NewGuid();

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var payloadKnown = await uow.PayloadHashExistsAsync(request.TenantId, payloadHash, cancellationToken);
            var existingHash = await uow.GetLatestHashForSourceReferenceAsync(request.TenantId, sourceReference, cancellationToken);

            // Décision lue AVANT insertion : correcte pour un drainage SÉRIE de l'agent (F12 §3.1).
            // Limite assumée sous concurrence sur une référence inédite : voir INV-INGESTION-016.
            var decision = DocumentIngestionDecision.Evaluate(payloadKnown, existingHash, payloadHash);

            if (!decision.IsAccepted)
            {
                // Doublon : aucun effet (idempotence — re-push complet après réinstallation d'agent).
                // TODO(ADR-0012) : sous l'acquittement agent en deux temps, ce `duplicate` devra DISTINGUER
                // « déjà rangé (Detected existe) » → terminal, de « reçu mais non rangé » → RE-TENTER le
                // rangement (idempotent). Sans cette distinction, un renvoi d'un document non rangé est
                // écarté ici et la fuite reste ouverte. Implémenté avec AGT + le point de statut (Ingestion).
                return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Duplicate);
            }

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

            // Événements écrits dans la MÊME transaction que l'inscription (cohérence transactionnelle) :
            // c'est le déclencheur DURABLE du traitement aval (DocumentReceived → PIP01 ; +altération → TRK03).
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
        }

        // Réception + événement DURABLEMENT committés. Création synchrone du document Detected (port
        // Documents — no-op jusqu'à TRK02) en BEST-EFFORT : un échec ici n'invalide pas la réception
        // (le document EST reçu, l'événement EST publié) ; le module Documents recréera le document de
        // façon idempotente sur DocumentId en consommant DocumentReceived. Appelé APRÈS commit pour
        // éviter tout document orphelin si l'inscription échoue/entre en course (contrat de cohérence
        // prérequis BLOQUANT de TRK02 — voir IDocumentIntake).
        await RegisterDetectedDocumentBestEffortAsync(documentId, request, sourceReference, payloadHash, document, receivedAt, cancellationToken);

        return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Accepted);
    }

    private async Task RegisterDetectedDocumentBestEffortAsync(
        Guid documentId,
        IngestDocumentBatchCommand request,
        string sourceReference,
        string payloadHash,
        PivotDocumentDto document,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        try
        {
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // TODO(ADR-0012) : avaler l'échec ici n'est SÛR que sous l'acquittement agent en deux temps —
            // l'agent garde l'élément « en cours » et le RENVOIE tant que le point de statut ne confirme pas
            // un état terminal, ce qui re-tente le rangement (idempotent). NE PAS « corriger » en rendant
            // cet intake bloquant (ré-introduirait le risque de document orphelin, prérequis TRK02) : le
            // bon correctif est le protocole de statut (AGT + Ingestion + PIP01), pas une transaction ici.
            LogDocumentIntakeFailed(_logger, documentId, ex);
        }
    }
}
