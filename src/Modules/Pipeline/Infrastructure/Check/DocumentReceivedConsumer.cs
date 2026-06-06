namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// CHECK (PIP01b) — consommateur DURABLE de <see cref="DocumentReceivedV1"/> (publié par l'ingestion via
/// l'outbox du socle). Pour CHAQUE document reçu : relit le contenu pivot stagé (PIP00, hash re-vérifié),
/// mappe la TVA ligne par ligne (table validée du tenant), valide le document enrichi, puis le fait passer
/// <c>Detected → ReadyToSend</c> (mapping OK + validation OK) ou <c>Detected → Blocked</c> (motif persisté
/// dans la piste d'audit append-only du module Documents). Une trace d'exécution est consignée
/// (<c>pipeline.run_logs</c>).
/// </summary>
/// <remarks>
/// <para>Le worker d'outbox dispatche en scope SYSTÈME (aucun tenant établi). On résout donc un scope
/// TENANT via <see cref="ITenantScopeFactory"/> (seam du Host — même mécanique que <c>TenantJobRunner</c>,
/// SOL06) à partir du slug porté par l'événement, et on résout les services métier (staging, mapping,
/// validation, cycle de vie, paramétrage) DEPUIS ce scope : ils sont alors routés vers la base du tenant
/// (database-per-tenant, blueprint §7). Le pipeline ne touche les autres modules QUE par leurs Contracts
/// (frontière P1, CLAUDE.md n°14) et ne référence AUCUN plug-in PA concret.</para>
/// <para>IDEMPOTENCE (livraison at-least-once) : CHECK n'agit que sur un document encore <c>Detected</c>.
/// Un rejeu d'un document déjà avancé est ignoré (aucune transition, aucune trace). Un contenu pas encore
/// stagé (<see cref="StagedPayloadNotFoundException"/>) est TRANSITOIRE (ADR-0014) : on laisse l'exception
/// se propager pour que l'outbox re-livre — JAMAIS un blocage terminal.</para>
/// <para>AUCUNE règle fiscale inventée (CLAUDE.md n°2) : le triplet {catégorie, taux, VATEX} vient de la
/// table validée du tenant (module TvaMapping) ; la part fournie au mapping est <see cref="TvaMappingPart.Autre"/>
/// (voir <see cref="CheckTvaMapping"/>). La GARDE-FOU production n'est JAMAIS affaiblie (CLAUDE.md n°3).</para>
/// </remarks>
public sealed partial class DocumentReceivedConsumer : IIntegrationEventConsumer<DocumentReceivedV1>
{
    /// <summary>Nom sérialisé de l'état <c>DocumentState.Detected</c> (exposé en chaîne par les Contracts du module Documents).</summary>
    private const string DetectedStateName = "Detected";

    /// <summary>
    /// Motif de blocage quand le contenu stagé est altéré ou illisible (échec d'intégrité). Contrairement à
    /// l'absence (transitoire), une corruption est PERSISTANTE : re-tenter relit le même blob et échoue à
    /// l'identique. On BLOQUE le document (visible opérateur, « bloquer plutôt qu'envoyer faux » — CLAUDE.md n°3)
    /// plutôt que de laisser l'outbox dead-letter en silence avec un document figé en Detected.
    /// </summary>
    private const string StagingIntegrityReason =
        "Le contenu stagé du document est altéré ou illisible (contrôle d'intégrité) : impossible de poursuivre " +
        "le contrôle sans risquer de transmettre une donnée fausse. Document bloqué. Action opérateur : relancez " +
        "l'extraction du document depuis le logiciel source (l'agent le re-poussera) ; si le problème persiste, " +
        "contactez le support.";

    private readonly ITenantScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentReceivedConsumer> _logger;

    /// <summary>Construit le consommateur CHECK (horloge système).</summary>
    public DocumentReceivedConsumer(ITenantScopeFactory scopeFactory, ILogger<DocumentReceivedConsumer> logger)
        : this(scopeFactory, logger, TimeProvider.System)
    {
    }

    /// <summary>Construit le consommateur CHECK avec une horloge explicite (tests : déterminisme).</summary>
    internal DocumentReceivedConsumer(
        ITenantScopeFactory scopeFactory,
        ILogger<DocumentReceivedConsumer> logger,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task HandleAsync(IntegrationEvent<DocumentReceivedV1> integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;

        await using var scope = _scopeFactory.Create(payload.TenantId);
        var services = scope.Services;

        // Idempotence (rejeu at-least-once) : CHECK n'agit que sur un document encore Detected.
        var documentQueries = services.GetRequiredService<IDocumentQueries>();
        var current = await documentQueries.GetByIdAsync(payload.DocumentId, cancellationToken);
        if (current is null)
        {
            // Document Detected + événement committés dans la même base : l'absence est une anomalie de
            // données, pas un cas nominal → échec (retry outbox), jamais un état inventé.
            throw new InvalidOperationException(
                $"CHECK : document {payload.DocumentId} (tenant « {payload.TenantId} ») introuvable alors qu'un DocumentReceivedV1 a été publié.");
        }

        if (!string.Equals(current.State, DetectedStateName, StringComparison.Ordinal))
        {
            LogAlreadyProcessed(_logger, payload.DocumentId, current.State);
            return;
        }

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : mapping/validation tenant-scopés impossibles.
            // Transitoire (le profil peut arriver) → retry outbox, jamais un blocage inventé.
            throw new InvalidOperationException(
                $"CHECK : aucun profil tenant (companyId) pour « {payload.TenantId} » — paramétrage tenant requis (CFG02). Re-tentable.");
        }

        var startedAt = _timeProvider.GetUtcNow();

        // 1) Relecture du contenu stagé (PIP00). Le magasin re-vérifie le hash. Absent = transitoire (ADR-0014).
        var staging = services.GetRequiredService<IPayloadStagingStore>();
        var key = new StagedPayloadKey(payload.TenantId, payload.DocumentId, payload.PayloadHash);
        string canonicalJson;
        try
        {
            canonicalJson = await staging.ReadAsync(key, cancellationToken);
        }
        catch (StagedPayloadNotFoundException)
        {
            // JAMAIS terminal (ADR-0014) : propager → l'outbox re-livrera l'événement (retry transitoire).
            LogStagingNotYetAvailable(_logger, payload.DocumentId, payload.TenantId);
            throw;
        }
        catch (StagedPayloadIntegrityException ex)
        {
            // Décision de routage déférée à PIP01 (cf. StagedPayloadIntegrityException) : on BLOQUE le document.
            // Les deux cas (HashMismatch = altération terminale ; Undecryptable = blob altéré OU clé Data
            // Protection indisponible) signifient un contenu NON DIGNE DE CONFIANCE → « bloquer plutôt qu'envoyer
            // faux » (CLAUDE.md n°3). Les clés DP sont un prérequis OPS PERSISTANT : une indisponibilité n'est pas
            // un transitoire nominal, et la bloquer la rend visible (motif + « contactez le support ») plutôt que
            // de la laisser dead-letter en silence sur un document figé en Detected. Le geste correctif (ré-extraire
            // → re-stage ré-encodé) résout les deux cas. C'est plus sûr qu'un retry à l'aveugle sur une altération.
            LogStagingIntegrityFailure(_logger, payload.DocumentId, payload.TenantId, ex);
            await BlockAndLogAsync(
                services, payload.DocumentId, WithDocumentNumber(current.DocumentNumber, StagingIntegrityReason), startedAt, cancellationToken);
            return;
        }

        var pivot = PivotCanonicalJsonReader.Read(canonicalJson);

        // 2) Décision CHECK (mapping TVA → garde-fou production → validation). Partagée avec la réconciliation
        //    des avoirs (PIP02, SendTenantJob) via DocumentCheckEvaluator : une SEULE source de la décision de
        //    blocage fiscal (deux implémentations divergentes seraient un risque de conformité — CLAUDE.md n°2/3).
        var decision = await DocumentCheckEvaluator.EvaluateAsync(
            services, companyId.Value, current.DocumentNumber, pivot, cancellationToken);

        // 3) Transition d'état + trace d'exécution. CHECK n'a vérifié l'état Detected qu'en amont : si une
        // course de rejeu a fait avancer le document entre-temps, la transition lèvera et l'événement sera
        // re-livré puis ignoré (le document ne sera plus Detected). On ne capture PAS le type d'exception du
        // Domain Documents (frontière Contracts-only, CLAUDE.md n°14) : on laisse l'outbox gérer la reprise.
        if (decision.IsReady)
        {
            // Snapshot de la ventilation TVA sourcée AVANT le passage ReadyToSend (ADR-0015 §4 : happened-before
            // — la version capturée est celle qui sera liée à l'émission). Idempotent (re-CHECK = pas de doublon).
            await WriteVentilationSnapshotAsync(services, payload.DocumentId, current, decision, cancellationToken);
            await MarkReadyToSendAndLogAsync(services, payload.DocumentId, decision.MappingVersion!, startedAt, cancellationToken);
        }
        else
        {
            await BlockAndLogAsync(services, payload.DocumentId, decision.BlockReason!, startedAt, cancellationToken);
        }
    }

    /// <summary>Préfixe un motif de blocage rédigé par CHECK avec le numéro de document (CLAUDE.md n°12).</summary>
    private static string WithDocumentNumber(string documentNumber, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"Document n° {documentNumber} : {reason}");

    [LoggerMessage(EventId = 7100, Level = LogLevel.Debug,
        Message = "CHECK ignoré pour le document {DocumentId} : état {State} (déjà traité ou avancé — idempotent).")]
    private static partial void LogAlreadyProcessed(ILogger logger, Guid documentId, string state);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Information,
        Message = "CHECK : contenu pas encore stagé pour le document {DocumentId} (tenant « {TenantId} ») — re-livraison ultérieure (transitoire, ADR-0014).")]
    private static partial void LogStagingNotYetAvailable(ILogger logger, Guid documentId, string tenantId);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Error,
        Message = "CHECK : contenu stagé altéré/illisible pour le document {DocumentId} (tenant « {TenantId} ») — document bloqué (intégrité, persistant).")]
    private static partial void LogStagingIntegrityFailure(ILogger logger, Guid documentId, string tenantId, Exception exception);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Information,
        Message = "CHECK terminé pour le document {DocumentId} (prêt à l'envoi : {Succeeded}).")]
    private static partial void LogCheckCompleted(ILogger logger, Guid documentId, bool succeeded);

    /// <summary>Bloque le document (motif persisté dans la piste d'audit append-only) et consigne l'exécution.</summary>
    private async Task BlockAndLogAsync(
        IServiceProvider services,
        Guid documentId,
        string reason,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await services.GetRequiredService<IDocumentLifecycle>().BlockAsync(documentId, reason, cancellationToken);
        await WriteRunLogAsync(
            services,
            startedAt,
            succeeded: false,
            string.Create(CultureInfo.InvariantCulture, $"CHECK {documentId} → Blocked."),
            cancellationToken);
        LogCheckCompleted(_logger, documentId, false);
    }

    /// <summary>Fait passer le document ReadyToSend (version de mapping consignée) et consigne l'exécution.</summary>
    private async Task MarkReadyToSendAndLogAsync(
        IServiceProvider services,
        Guid documentId,
        string mappingVersion,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await services.GetRequiredService<IDocumentLifecycle>().MarkReadyToSendAsync(documentId, mappingVersion, cancellationToken);
        await WriteRunLogAsync(
            services,
            startedAt,
            succeeded: true,
            string.Create(CultureInfo.InvariantCulture, $"CHECK {documentId} → ReadyToSend (table {mappingVersion})."),
            cancellationToken);
        LogCheckCompleted(_logger, documentId, true);
    }

    /// <summary>
    /// Capture le snapshot de la ventilation TVA sourcée du document (ADR-0015) dans la persistance dédiée
    /// (append-only, tenant-scopée, distincte du staging et du WORM). Écriture IDEMPOTENTE sur
    /// (document_id, mapping_version) : un re-CHECK n'insère pas de doublon. Ne porte QUE la sortie du mapping
    /// validé (INV-VENTILATION-001) — aucune dérivation. Permet l'agrégation de paiement (PIP03a) APRÈS la purge
    /// du staging.
    /// </summary>
    private async Task WriteVentilationSnapshotAsync(
        IServiceProvider services,
        Guid documentId,
        DocumentDto document,
        CheckDecision decision,
        CancellationToken cancellationToken)
    {
        var snapshot = new VentilationSnapshot
        {
            DocumentId = documentId,
            DocumentNumber = document.DocumentNumber,
            SourceReference = document.SourceReference,
            OperationCategory = decision.OperationCategory!.Value,
            MappingVersion = decision.MappingVersion!,
            Lines = decision.Ventilation!,
            CreatedUtc = _timeProvider.GetUtcNow(),
        };

        await services.GetRequiredService<IVentilationSnapshotStore>().SaveAsync(snapshot, cancellationToken);
    }

    /// <summary>Écrit une trace d'exécution CHECK clôturée (1 document) dans <c>pipeline.run_logs</c> du tenant.</summary>
    private async Task WriteRunLogAsync(
        IServiceProvider services,
        DateTimeOffset startedAt,
        bool succeeded,
        string detail,
        CancellationToken cancellationToken)
    {
        var runLog = RunLog.Start(PipelineRunType.Check, PipelineRunTrigger.Event, startedAt);
        runLog.Complete(
            completedAt: _timeProvider.GetUtcNow(),
            documentsProcessed: 1,
            documentsSucceeded: succeeded ? 1 : 0,
            documentsFailed: succeeded ? 0 : 1,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }
}
