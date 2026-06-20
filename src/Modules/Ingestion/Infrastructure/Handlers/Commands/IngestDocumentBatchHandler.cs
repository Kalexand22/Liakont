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
using Liakont.Modules.Staging.Contracts;
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
    private readonly IExtractorCapabilitiesWriter _extractorCapabilitiesWriter;
    private readonly IDocumentIntake _documentIntake;
    private readonly IPayloadStagingStore _stagingStore;
    private readonly ILogger<IngestDocumentBatchHandler> _logger;

    public IngestDocumentBatchHandler(
        IReceivedDocumentUnitOfWorkFactory uowFactory,
        ISourceTaxRegimeWriter sourceTaxRegimeWriter,
        IExtractorCapabilitiesWriter extractorCapabilitiesWriter,
        IDocumentIntake documentIntake,
        IPayloadStagingStore stagingStore,
        ILogger<IngestDocumentBatchHandler> logger)
    {
        _uowFactory = uowFactory;
        _sourceTaxRegimeWriter = sourceTaxRegimeWriter;
        _extractorCapabilitiesWriter = extractorCapabilitiesWriter;
        _documentIntake = documentIntake;
        _stagingStore = stagingStore;
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

        // 2. Métadonnée SECONDAIRE (régimes source pour TVA03 + capacités déclarées pour RD401/RD403),
        //    best-effort : son échec ne casse pas l'ingestion primaire déjà committée document par document.
        await PersistSourceTaxRegimesBestEffortAsync(request, cancellationToken);
        await PersistExtractorCapabilitiesBestEffortAsync(request, cancellationToken);

        return new PushBatchResponseDto(results);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persistance des régimes de TVA source échouée pour le tenant {TenantId} (métadonnée best-effort, ingestion des documents non affectée)")]
    private static partial void LogSourceTaxRegimePersistFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persistance des capacités de source déclarées échouée pour l'agent {AgentId} du tenant {TenantId} (métadonnée best-effort, ingestion des documents non affectée)")]
    private static partial void LogExtractorCapabilitiesPersistFailed(ILogger logger, Guid agentId, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Création du document Detected (port IDocumentIntake) échouée pour {DocumentId} (best-effort ; le déclencheur durable reste l'événement DocumentReceived déjà publié)")]
    private static partial void LogDocumentIntakeFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-rangement d'un doublon reçu mais non rangé échoué pour {DocumentId} (best-effort, non bloquant ; le renvoi de l'agent re-tentera — ADR-0012)")]
    private static partial void LogReRangingFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Contenu stagé re-fourni pour le document rangé {DocumentId} dont le staging était absent (re-push agent — FIX07b/ADR-0014) ; le CHECK re-tournera à la re-livraison de l'événement.")]
    private static partial void LogReStagedLostContent(ILogger logger, Guid documentId);

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

    private async Task PersistExtractorCapabilitiesBestEffortAsync(IngestDocumentBatchCommand request, CancellationToken cancellationToken)
    {
        // Add-only : un agent N-1 ne transmet aucune capacité (null) → rien à persister, on ne touche pas
        // la dernière déclaration connue (pas d'écrasement par l'absence).
        if (request.ExtractorCapabilities is not { } capabilities)
        {
            return;
        }

        var record = new ExtractorCapabilitiesRecord
        {
            ProvidesSourceDocuments = capabilities.ProvidesSourceDocuments,
            ProvidesUnlinkedDocumentPool = capabilities.ProvidesUnlinkedDocumentPool,
            HasDetailedLines = capabilities.HasDetailedLines,
            HasCreditNoteLink = capabilities.HasCreditNoteLink,
            ExposesPayments = capabilities.ExposesPayments,
            RegimeKeyShape = capabilities.RegimeKeyShape,
            EmitterIdentitySource = capabilities.EmitterIdentitySource,
            HasStoredHeaderTotal = capabilities.HasStoredHeaderTotal,
            IsMutableAfterIssue = capabilities.IsMutableAfterIssue,
            NumberUniquenessScope = capabilities.NumberUniquenessScope,
        };

        try
        {
            await _extractorCapabilitiesWriter.UpsertAsync(request.TenantId, request.AgentId, record, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogExtractorCapabilitiesPersistFailed(_logger, request.AgentId, request.TenantId, ex);
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

        string canonicalJson;
        string payloadHash;
        try
        {
            // Sérialisation canonique UNE seule fois (ADR-0007) : sert l'empreinte ET le contenu stagé (PIP00).
            canonicalJson = CanonicalJson.Serialize(document);
            payloadHash = PayloadHasher.ComputeHash(canonicalJson);
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

        // Identité d'origine d'un doublon par empreinte (affinage du dédoublonnage, ADR-0012) — résolue dans la
        // transaction, exploitée APRÈS (hors transaction), comme le rangement best-effort du fast-path accepté.
        Guid? duplicateOriginalId = null;
        bool accepted;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var payloadKnown = await uow.PayloadHashExistsAsync(request.TenantId, payloadHash, cancellationToken);
            var existingHash = await uow.GetLatestHashForSourceReferenceAsync(request.TenantId, sourceReference, cancellationToken);

            // Décision lue AVANT insertion : correcte pour un drainage SÉRIE de l'agent (F12 §3.1).
            // Limite assumée sous concurrence sur une référence inédite : voir INV-INGESTION-016.
            var decision = DocumentIngestionDecision.Evaluate(payloadKnown, existingHash, payloadHash);

            if (!decision.IsAccepted)
            {
                // Doublon par empreinte. AFFINAGE DÉDOUBLONNAGE (ADR-0012) : on NE renvoie plus aveuglément
                // « duplicate ». On capture l'identité d'origine pour, APRÈS la transaction, DISTINGUER
                // « déjà rangé (Detected existe) » → terminal, de « reçu mais non rangé » → RE-TENTER le
                // rangement (idempotent) — ce qui ferme la perte silencieuse d'un document reçu mais jamais
                // entré dans le pipeline. Le registre de réception reste append-only (aucune écriture ici).
                duplicateOriginalId = await uow.GetDocumentIdByPayloadHashAsync(request.TenantId, payloadHash, cancellationToken);
                accepted = false;
            }
            else
            {
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
                    // On traite comme doublon (aucun événement publié pour cette tentative) ; le push gagnant range
                    // le document, et un renvoi ultérieur re-tentera le rangement si besoin (ADR-0012).
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

                // PIP00 / ADR-0014 — INVARIANT D'ORDRE (pas d'atomicité) : le pivot COMPLET est stagé (écrit +
                // contenu flushé sur disque) AVANT le commit du registre + de l'événement outbox. Il n'existe
                // pas de transaction distribuée (XA/2PC) entre un blob store et Postgres : c'est un ordre, pas
                // une atomicité. Conséquences :
                //  - le pipeline CHECK/SEND relira le pivot depuis le staging (fin de la supposition « le contenu
                //    est déjà là ») ;
                //  - un échec de staging (disque) remonte avant le commit → la transaction est annulée (rollback)
                //    → au pire un blob ORPHELIN (purgeable, adressé par CONTENU donc ré-écrit idempotemment au
                //    renvoi de l'agent), jamais un événement sans contenu ; le contenu n'est plus jamais jeté ;
                //  - NUANCE : le renommage atomique qui publie le blob n'est pas fsyncé (pas d'API portable .NET) ;
                //    sous coupure d'alimentation entre ce renommage et le commit Postgres, l'événement peut
                //    survivre sans le blob — PAS une perte : le FILET DE SÉCURITÉ de l'agent (ADR-0014) re-pousse
                //    jusqu'au statut Processed, le pivot est re-stagé.
                await _stagingStore.WriteAsync(
                    new StagedPayloadKey(request.TenantId, documentId, payloadHash),
                    canonicalJson,
                    cancellationToken);

                await uow.CommitAsync(cancellationToken);
                accepted = true;
            }
        }

        if (!accepted)
        {
            // Doublon par empreinte : re-tenter le rangement d'un document reçu mais NON rangé (ADR-0012) — ou
            // re-stager un document rangé dont le staging a été perdu (FIX07b) — jamais écarter aveuglément.
            // Hors transaction (comme le rangement best-effort accepté).
            await RetryRangingIfNotRangedAsync(duplicateOriginalId, request, sourceReference, payloadHash, document, receivedAt, canonicalJson, cancellationToken);
            return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Duplicate);
        }

        // Réception + événement DURABLEMENT committés. Création synchrone du document Detected (port
        // Documents) en BEST-EFFORT : un échec ici n'invalide pas la réception (le document EST reçu,
        // l'événement EST publié) ; le module Documents recréera le document de façon idempotente sur
        // DocumentId en consommant DocumentReceived. Appelé APRÈS commit pour éviter tout document orphelin
        // si l'inscription échoue/entre en course (contrat de cohérence prérequis BLOQUANT de TRK02).
        await RegisterDetectedDocumentBestEffortAsync(documentId, request, sourceReference, payloadHash, document, receivedAt, cancellationToken);

        return new DocumentPushResultDto(sourceReference, DocumentPushStatus.Accepted);
    }

    /// <summary>
    /// AFFINAGE DÉDOUBLONNAGE (ADR-0012) + RÉHYDRATATION DU STAGING PERDU (FIX07b) : sur un doublon par
    /// empreinte, on NE renvoie « duplicate » sans effet QUE pour un VRAI doublon terminal — <c>Detected</c>
    /// présent ET contenu encore stagé. Sinon on RE-STAGE le pivot (idempotent, content-addressed) :
    /// <list type="bullet">
    ///   <item>« reçu mais NON rangé » (ADR-0012) → re-stage puis re-range (idempotent sur DocumentId) ;</item>
    ///   <item>« rangé mais staging PERDU » (FIX07b) → document ZOMBIE (Detected sans contenu, CHECK bloqué) :
    ///   le re-push de l'agent re-fournit le même contenu, on le re-stage pour le rendre re-traitable au lieu
    ///   d'écarter aveuglément.</item>
    /// </list>
    /// Jamais une résurrection altérée : ce chemin n'est atteint que parce que l'empreinte est déjà connue, et la
    /// clé de staging porte cette même empreinte (re-vérifiée à la relecture par le magasin) — un contenu altéré
    /// aurait une empreinte différente, donc le chemin ACCEPTÉ (altération), pas celui-ci. Le re-push de l'agent ne
    /// concerne que les éléments pas encore « Processed » (staged+Detected) de SON point de vue (ADR-0014). Le
    /// re-stage réhydrate donc les états PRÉ-ÉMISSION dont le staging a réellement été perdu (Detected mais aussi
    /// ReadyToSend/Blocked, qui en ont encore besoin pour le SEND/recheck — INV-STAGING-007). CAS RÉSIDUEL : un
    /// document déjà <c>Issued</c> (staging légitimement purgé après WORM, ADR-0014 §4) n'atteint ce chemin qu'en
    /// reprise après PERTE D'ÉTAT de l'agent (re-scan/re-push). Le re-stage y est SANS EFFET FONCTIONNEL — aucun
    /// événement <c>DocumentReceived</c> ne subsiste dans l'outbox (ni re-range ni ré-émission ; le document reste
    /// <c>Issued</c>) — mais il ré-écrit un blob qui ne repassera PAS par la purge du SEND
    /// (<see cref="IStagingPurgeService"/> ne purge qu'une fois, pendant le SEND). Ce cas (rare,
    /// idempotent, borné par document) relève de la DETTE « croissance non bornée du staging » (Staging/MODULE.md,
    /// propriétaire PIP01), PAS d'une re-purge automatique : le borner ici exigerait la connaissance du cycle de
    /// vie / de la présence WORM dans le chemin chaud d'intake et risquerait de re-casser la réhydratation des
    /// états pré-émission ci-dessus.
    /// </summary>
    private async Task RetryRangingIfNotRangedAsync(
        Guid? originalDocumentId,
        IngestDocumentBatchCommand request,
        string sourceReference,
        string payloadHash,
        PivotDocumentDto document,
        DateTimeOffset receivedAt,
        string canonicalJson,
        CancellationToken cancellationToken)
    {
        if (originalDocumentId is not { } documentId)
        {
            // Empreinte connue mais aucune entrée de réception retrouvée (cas dégénéré) : rien à re-ranger.
            return;
        }

        // BEST-EFFORT, NON BLOQUANT (INV-INGESTION-018) : le re-rangement est le chemin DOMINANT en régime
        // permanent (le filet de sécurité de l'agent re-pousse tout → majorité de doublons). Un hoquet transitoire
        // de la base tenant (probe) ou du disque (re-stage) NE DOIT PAS faire échouer le lot ni perdre les résultats
        // par document déjà committés : on avale, on journalise, l'agent renvoie au prochain cycle (ADR-0012).
        try
        {
            var stagingKey = new StagedPayloadKey(request.TenantId, documentId, payloadHash);
            var ranged = await _documentIntake.IsDocumentRangedAsync(documentId, request.TenantId, cancellationToken);

            // VRAI doublon TERMINAL : déjà rangé ET contenu stagé encore présent → aucun effet (le contenu de la
            // 1re réception suffit). Chemin DOMINANT en régime permanent.
            if (ranged && await _stagingStore.ExistsAsync(stagingKey, cancellationToken))
            {
                return;
            }

            // Sinon, RE-STAGE (idempotent, content-addressed) : « reçu mais non rangé » OU « rangé mais staging
            // perdu » (zombie — FIX07b). Le contenu n'est plus jamais jeté.
            await _stagingStore.WriteAsync(stagingKey, canonicalJson, cancellationToken);

            if (ranged)
            {
                // Zombie réhydraté : le Detected existe déjà (ne pas re-ranger). Le CHECK re-tournera à la
                // re-livraison de l'événement DocumentReceived d'origine tant qu'il est dans l'outbox ; s'il a déjà
                // été dead-letté (≥ MaxRetries de CHECK sur staging absent), le rejeu est un geste opérateur
                // documenté (ADR-0014, amendement FIX07b) — aucun rejeu automatique n'est inventé ici.
                LogReStagedLostContent(_logger, documentId);
            }
            else
            {
                // Reçu mais NON rangé : ranger maintenant, avec l'IDENTITÉ DE LA RÉCEPTION D'ORIGINE (idempotent sur DocumentId).
                await RegisterDetectedDocumentBestEffortAsync(documentId, request, sourceReference, payloadHash, document, receivedAt, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le document reste « reçu mais non rangé » (ou son staging non réhydraté) ; le renvoi de l'agent re-tentera (jamais une perte).
            LogReRangingFailed(_logger, documentId, ex);
        }
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
