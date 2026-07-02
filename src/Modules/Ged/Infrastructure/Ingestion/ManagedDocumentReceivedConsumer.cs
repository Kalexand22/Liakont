namespace Liakont.Modules.Ged.Infrastructure.Ingestion;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Contracts.Events;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Consommateur DURABLE de <see cref="ManagedDocumentReceivedV1"/> (publié par l'ingestion GED via l'outbox du socle),
/// item GED05b (F19 §2.4). Pour CHAQUE document géré reçu : relit le pivot GED stagé (hash re-vérifié), puis délègue
/// l'indexation au foyer d'écriture UNIQUE <see cref="IGedDocumentIndexer"/> (charge le PROFIL VALIDÉ, applique
/// <c>GedMapper</c>, écrit <c>indexed</c>/<c>deferred</c> sous garde de concurrence). AUCUN état fiscal n'est atteint
/// (pas de <c>documents.documents</c>, pas de <c>DocumentReceivedConsumer</c>).
/// </summary>
/// <remarks>
/// <para>Le worker d'outbox dispatche en scope SYSTÈME (aucun tenant établi). On résout un scope TENANT via
/// <see cref="ITenantScopeFactory"/> (seam du Host, comme <c>DocumentReceivedConsumer</c>) à partir du slug porté par
/// l'événement, et on résout les services (staging, indexeur) DEPUIS ce scope : ils sont routés vers la base du tenant
/// (database-per-tenant, blueprint §7). Le module GED ne référence AUCUN module fiscal (frontière F19 §6, RL-01).</para>
/// <para>IDEMPOTENCE + CONCURRENCE (livraison at-least-once, RL-04) : la garde par document vit dans l'indexeur
/// (<c>pg_advisory_xact_lock</c> + statut lu) — un document déjà <c>indexed</c>/<c>deferred</c> est un no-op (replay),
/// et deux livraisons SIMULTANÉES sont sérialisées. Un contenu pas encore stagé
/// (<see cref="StagedPayloadNotFoundException"/>) est TRANSITOIRE (ADR-0014) : on propage pour re-livraison, jamais un
/// blocage terminal ; un contenu stagé ALTÉRÉ (<see cref="StagedPayloadIntegrityException"/>) est persistant → le
/// document est directement déféré (jamais un retry aveugle).</para>
/// </remarks>
public sealed partial class ManagedDocumentReceivedConsumer : IIntegrationEventConsumer<ManagedDocumentReceivedV1>
{
    /// <summary>Provenance des liens écrits pour le canal d'ingestion GED (agent) — <c>ck_dal_source</c>.</summary>
    private const string IngestionSource = "agent";

    /// <summary>Motif de déférement quand le contenu stagé est altéré/illisible (persistant — jamais un retry aveugle).</summary>
    private const string StagingIntegrityReason =
        "Le contenu stagé du document GED est altéré ou illisible (contrôle d'intégrité) : indexation impossible sans " +
        "risquer une donnée fausse. Document rangé en attente (deferred). Action opérateur : relancez l'extraction du " +
        "document depuis le logiciel source (l'agent le re-poussera) ; si le problème persiste, contactez le support.";

    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ILogger<ManagedDocumentReceivedConsumer> _logger;

    public ManagedDocumentReceivedConsumer(ITenantScopeFactory scopeFactory, ILogger<ManagedDocumentReceivedConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(IntegrationEvent<ManagedDocumentReceivedV1> integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;

        await using var scope = _scopeFactory.Create(payload.TenantId);
        var services = scope.Services;
        var indexer = services.GetRequiredService<IGedDocumentIndexer>();

        // 1) Relecture du contenu stagé (le magasin re-vérifie le hash). Absent = transitoire (ADR-0014) ; altéré = persistant.
        var staging = services.GetRequiredService<IPayloadStagingStore>();
        var key = new StagedPayloadKey(payload.TenantId, payload.ManagedDocumentId, payload.PayloadHash);
        string canonicalJson;
        try
        {
            canonicalJson = await staging.ReadAsync(key, cancellationToken);
        }
        catch (StagedPayloadNotFoundException)
        {
            LogStagingNotYetAvailable(_logger, payload.ManagedDocumentId, payload.TenantId);
            throw;
        }
        catch (StagedPayloadIntegrityException ex)
        {
            LogStagingIntegrityFailure(_logger, payload.ManagedDocumentId, payload.TenantId, ex);
            await indexer.IndexDeferredAsync(
                payload.ManagedDocumentId, payload.SourceReference, docKind: null, StagingIntegrityReason, cancellationToken: cancellationToken);
            return;
        }

        // 2) Indexation via le foyer d'écriture unique (mapping + écriture idempotente/concurrente en base tenant).
        var ingested = GedCanonicalJsonReader.Read(canonicalJson);
        await indexer.IndexAsync(new GedIndexRequest(payload.ManagedDocumentId, ingested, IngestionSource), cancellationToken);
    }

    [LoggerMessage(EventId = 7311, Level = LogLevel.Information,
        Message = "Indexation GED : contenu pas encore stagé pour le document {ManagedDocumentId} (tenant « {TenantId} ») — re-livraison ultérieure (transitoire, ADR-0014).")]
    private static partial void LogStagingNotYetAvailable(ILogger logger, Guid managedDocumentId, string tenantId);

    [LoggerMessage(EventId = 7312, Level = LogLevel.Error,
        Message = "Indexation GED : contenu stagé altéré/illisible pour le document {ManagedDocumentId} (tenant « {TenantId} ») — document déféré (intégrité, persistant).")]
    private static partial void LogStagingIntegrityFailure(ILogger logger, Guid managedDocumentId, string tenantId, Exception exception);
}
