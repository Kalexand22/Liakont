namespace Liakont.Modules.Ged.Infrastructure.Ingestion;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Ged;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Application.Mapping;
using Liakont.Modules.Ged.Contracts.Events;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Domain.Index;
using Liakont.Modules.Ged.Domain.Mapping;
using Liakont.Modules.Ged.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Consommateur DURABLE de <see cref="ManagedDocumentReceivedV1"/> (publié par l'ingestion GED via l'outbox du socle),
/// item GED05b (F19 §2.4). Pour CHAQUE document géré reçu : relit le pivot GED stagé (hash re-vérifié), charge le
/// PROFIL VALIDÉ de son <c>documentType</c>, applique <see cref="GedMapper"/> (mappé ou DÉFÉREMENT), puis — sous garde
/// de concurrence par document — UPSERT le <c>ManagedDocument</c> (au statut <c>indexed</c>/<c>deferred</c>) + ses
/// liens d'axe et d'entité, dans UNE MÊME TRANSACTION en base TENANT. AUCUN état fiscal n'est atteint (pas de
/// <c>documents.documents</c>, pas de <c>DocumentReceivedConsumer</c>).
/// </summary>
/// <remarks>
/// <para>Le worker d'outbox dispatche en scope SYSTÈME (aucun tenant établi). On résout un scope TENANT via
/// <see cref="ITenantScopeFactory"/> (seam du Host, comme <c>DocumentReceivedConsumer</c>) à partir du slug porté par
/// l'événement, et on résout les services (staging, catalogues, profils, UoW d'index) DEPUIS ce scope : ils sont routés
/// vers la base du tenant (database-per-tenant, blueprint §7). Le module GED ne référence AUCUN module fiscal
/// (frontière F19 §6, RL-01).</para>
/// <para>IDEMPOTENCE + CONCURRENCE (livraison at-least-once, RL-04) : <see cref="IGedIndexUnitOfWork.BeginDocumentIndexingAsync"/>
/// prend un verrou consultatif transactionnel sur le document et lit son statut ; un document déjà <c>indexed</c>/<c>deferred</c>
/// est un no-op (replay), et deux livraisons SIMULTANÉES sont sérialisées (une seule écrit les liens). Un contenu pas
/// encore stagé (<see cref="StagedPayloadNotFoundException"/>) est TRANSITOIRE (ADR-0014) : on propage pour re-livraison,
/// jamais un blocage terminal.</para>
/// <para>DEFER PLUTÔT QUE DEVINER (INV-GED-05, n°3) : profil absent/non validé, axe obligatoire non résolu, valeur
/// ambiguë (mapper) OU type d'entité déclaré inconnu/inactif du catalogue (consommateur) OU contenu stagé altéré ⇒ le
/// document est rangé <c>deferred</c> avec un motif FRANÇAIS actionnable (§4.5, n°12), jamais mappé au hasard ni rejeté
/// en silence. Les valeurs d'axe <c>number</c> restent en <c>decimal</c> half-up (n°1, via <c>ValueNormalizer</c>).</para>
/// </remarks>
public sealed partial class ManagedDocumentReceivedConsumer : IIntegrationEventConsumer<ManagedDocumentReceivedV1>
{
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
            await IndexDeferredAsync(services, payload.ManagedDocumentId, payload.SourceReference, docKind: null, StagingIntegrityReason, cancellationToken);
            return;
        }

        var ingested = GedCanonicalJsonReader.Read(canonicalJson);

        // 2) Mapping déclaratif (profil VALIDÉ du tenant). Pré-résolution des axes (impédance sync/async : le mapper est
        //    PUR/sync, IAxisCatalog est async) → catalogue en mémoire + définitions conservées pour l'écriture des liens.
        var profile = await services.GetRequiredService<IGedMappingProfileStore>()
            .GetValidatedProfileAsync(ingested.DocumentType, cancellationToken);
        var (mappingCatalog, axisDefinitions) = await ResolveAxesAsync(services, profile, cancellationToken);
        var result = GedMapper.Map(profile, ingested, mappingCatalog);

        if (result.IsDeferred)
        {
            await IndexDeferredAsync(services, payload.ManagedDocumentId, ingested.SourceReference, ingested.DocumentType, result.DeferReason!, cancellationToken);
            return;
        }

        var document = result.Document!;

        // 3) Résolution des TYPES d'entité déclarés (§4.4) AVANT toute écriture : un type inconnu/inactif du catalogue
        //    DÉFÈRE le document (jamais deviner, règle 2/n°3), plutôt qu'une indexation partielle silencieuse.
        var entityTypes = new Dictionary<string, EntityTypeDefinition>(StringComparer.Ordinal);
        var entityCatalog = services.GetRequiredService<IEntityCatalog>();
        foreach (var code in document.Entities.Select(e => e.EntityType)
                     .Concat(document.Relations.Select(r => r.TargetType))
                     .Distinct(StringComparer.Ordinal))
        {
            var definition = await entityCatalog.ResolveAsync(code, cancellationToken);
            if (definition is null || !definition.IsActive)
            {
                var reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Le document « {ingested.SourceReference} » réfère un type d'entité « {code} » inconnu ou inactif du catalogue : document rangé en attente (deferred). Action opérateur : déclarer/activer le type d'entité en console.");
                await IndexDeferredAsync(services, payload.ManagedDocumentId, ingested.SourceReference, ingested.DocumentType, reason, cancellationToken);
                return;
            }

            entityTypes[code] = definition;
        }

        // 4) Écriture idempotente/concurrente en base TENANT (UNE transaction).
        var unitOfWorkFactory = services.GetRequiredService<IGedIndexUnitOfWorkFactory>();
        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken);

        var currentStatus = await unitOfWork.BeginDocumentIndexingAsync(payload.ManagedDocumentId, cancellationToken);
        if (currentStatus is not null)
        {
            // Replay (déjà indexed/deferred) : no-op. Le dispose de l'UoW rend le verrou consultatif (rollback sans écriture).
            LogAlreadyIndexed(_logger, payload.ManagedDocumentId, currentStatus);
            return;
        }

        await unitOfWork.UpsertManagedDocumentAsync(
            new ManagedDocument(payload.ManagedDocumentId, ingested.SourceReference, ingested.DocumentType, status: "indexed"),
            cancellationToken);

        foreach (var axisValue in document.Axes)
        {
            var definition = axisDefinitions[axisValue.AxisCode];
            var link = new DocumentAxisLink(payload.ManagedDocumentId, definition.Id, axisValue.Value, IngestionSource);
            await unitOfWork.AppendAxisLinkAsync(link, isSingleValued: !definition.IsMultiValue, cancellationToken);
        }

        // Entité DÉCLARÉE : résolue/créée dans le registre puis RATTACHÉE au document dans le rôle = son type
        // (le mapper ne porte pas de rôle explicite pour une entité ; le type est le rôle de rattachement — F19 §4.5).
        foreach (var entity in document.Entities)
        {
            var definition = entityTypes[entity.EntityType];
            var display = string.IsNullOrWhiteSpace(entity.Display) ? entity.ExternalId : entity.Display;
            var entityId = await unitOfWork.ResolveOrCreateEntityAsync(
                definition.Id, IdentityValue(definition, entity.ExternalId), display, IngestionSource, cancellationToken);
            await unitOfWork.AppendDocumentEntityLinkAsync(
                new DocumentEntityLink(payload.ManagedDocumentId, entityId, role: entity.EntityType, IngestionSource), cancellationToken);
        }

        // Relation DÉCLARÉE (document→entité cible) : la cible est résolue/créée, le lien porte le rôle = la NATURE
        // déclarée par le profil (Kind), jamais une valeur inventée (F19 §4.5).
        foreach (var relation in document.Relations)
        {
            var definition = entityTypes[relation.TargetType];
            var entityId = await unitOfWork.ResolveOrCreateEntityAsync(
                definition.Id, IdentityValue(definition, relation.TargetExternalId), relation.TargetExternalId, IngestionSource, cancellationToken);
            await unitOfWork.AppendDocumentEntityLinkAsync(
                new DocumentEntityLink(payload.ManagedDocumentId, entityId, role: relation.Kind, IngestionSource), cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        LogIndexed(_logger, payload.ManagedDocumentId, document.Axes.Count, document.Entities.Count + document.Relations.Count);
    }

    // §4.4 — clé d'identité de l'entité : normalisée (NFC + trim, comme une valeur de texte libre — jamais deviner) si
    // le type déclare une clé de résolution ; null sinon (pas de déduplication auto, création par observation).
    private static string? IdentityValue(EntityTypeDefinition definition, string externalId) =>
        definition.IdentityKey is null ? null : CanonicalJsonWriter.NormalizeToNfc(externalId).Trim();

    private static async Task<(IAxisMappingCatalog Catalog, Dictionary<string, AxisDefinition> Definitions)> ResolveAxesAsync(
        IServiceProvider services,
        GedMappingProfile? profile,
        CancellationToken cancellationToken)
    {
        var targets = new Dictionary<string, AxisMappingTarget>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, AxisDefinition>(StringComparer.Ordinal);
        if (profile is null)
        {
            return (new DictionaryAxisMappingCatalog(targets), definitions);
        }

        var axisCatalog = services.GetRequiredService<IAxisCatalog>();
        foreach (var code in profile.AxisRules.Select(r => r.AxisCode).Distinct(StringComparer.Ordinal))
        {
            var definition = await axisCatalog.ResolveAsync(code, cancellationToken);

            // Un axe inconnu OU inactif reste ABSENT du catalogue de mapping → le mapper rend null puis DÉFÈRE
            // (contrat IAxisMappingCatalog « inconnu ou inactif → null », jamais deviner).
            if (definition is null || !definition.IsActive)
            {
                continue;
            }

            targets[code] = new AxisMappingTarget(definition.Code, definition.DataType, definition.ValueScale);
            definitions[code] = definition;
        }

        return (new DictionaryAxisMappingCatalog(targets), definitions);
    }

    [LoggerMessage(EventId = 7310, Level = LogLevel.Information,
        Message = "Indexation GED ignorée pour le document {ManagedDocumentId} : statut {Status} (déjà indexé/déféré — idempotent, RL-04).")]
    private static partial void LogAlreadyIndexed(ILogger logger, Guid managedDocumentId, string status);

    [LoggerMessage(EventId = 7311, Level = LogLevel.Information,
        Message = "Indexation GED : contenu pas encore stagé pour le document {ManagedDocumentId} (tenant « {TenantId} ») — re-livraison ultérieure (transitoire, ADR-0014).")]
    private static partial void LogStagingNotYetAvailable(ILogger logger, Guid managedDocumentId, string tenantId);

    [LoggerMessage(EventId = 7312, Level = LogLevel.Error,
        Message = "Indexation GED : contenu stagé altéré/illisible pour le document {ManagedDocumentId} (tenant « {TenantId} ») — document déféré (intégrité, persistant).")]
    private static partial void LogStagingIntegrityFailure(ILogger logger, Guid managedDocumentId, string tenantId, Exception exception);

    [LoggerMessage(EventId = 7313, Level = LogLevel.Information,
        Message = "Indexation GED : document {ManagedDocumentId} indexé ({AxisCount} axe(s), {EntityLinkCount} lien(s) d'entité).")]
    private static partial void LogIndexed(ILogger logger, Guid managedDocumentId, int axisCount, int entityLinkCount);

    [LoggerMessage(EventId = 7314, Level = LogLevel.Information,
        Message = "Indexation GED : document {ManagedDocumentId} rangé en attente (deferred) — {Reason}")]
    private static partial void LogDeferred(ILogger logger, Guid managedDocumentId, string reason);

    private async Task IndexDeferredAsync(
        IServiceProvider services,
        Guid managedDocumentId,
        string sourceReference,
        string? docKind,
        string deferReason,
        CancellationToken cancellationToken)
    {
        var unitOfWorkFactory = services.GetRequiredService<IGedIndexUnitOfWorkFactory>();
        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken);

        var currentStatus = await unitOfWork.BeginDocumentIndexingAsync(managedDocumentId, cancellationToken);
        if (currentStatus is not null)
        {
            LogAlreadyIndexed(_logger, managedDocumentId, currentStatus);
            return;
        }

        await unitOfWork.UpsertManagedDocumentAsync(
            new ManagedDocument(managedDocumentId, sourceReference, docKind, status: "deferred", deferReason: deferReason),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        LogDeferred(_logger, managedDocumentId, deferReason);
    }

    /// <summary>Catalogue de mapping d'axes EN MÉMOIRE (axes pré-résolus depuis le catalogue tenant) fourni au mapper PUR.</summary>
    private sealed class DictionaryAxisMappingCatalog : IAxisMappingCatalog
    {
        private readonly IReadOnlyDictionary<string, AxisMappingTarget> _targets;

        public DictionaryAxisMappingCatalog(IReadOnlyDictionary<string, AxisMappingTarget> targets) => _targets = targets;

        public AxisMappingTarget? Resolve(string axisCode) =>
            _targets.TryGetValue(axisCode, out var target) ? target : null;
    }
}
