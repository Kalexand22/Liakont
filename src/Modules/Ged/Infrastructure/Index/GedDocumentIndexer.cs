namespace Liakont.Modules.Ged.Infrastructure.Index;

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
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Domain.Index;
using Liakont.Modules.Ged.Domain.Mapping;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implémentation du foyer d'écriture de l'index GED (tenant-scopée par ses dépendances scoped). Extraite du
/// consommateur d'ingestion GED05b pour être PARTAGÉE avec le backfill rétroactif GED10 : un seul chemin
/// d'indexation (mapping, validation d'entités, écriture <c>indexed</c>/<c>deferred</c> sous garde de concurrence),
/// jamais deux implémentations qui divergeraient. Ne crée PAS le scope tenant (l'appelant le fournit) ; ne lit PAS
/// le staging (spécifique au canal d'ingestion — reste dans le consommateur).
/// </summary>
internal sealed partial class GedDocumentIndexer : IGedDocumentIndexer
{
    private readonly IGedMappingProfileStore _profileStore;
    private readonly IAxisCatalog _axisCatalog;
    private readonly IEntityCatalog _entityCatalog;
    private readonly IGedIndexUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<GedDocumentIndexer> _logger;

    public GedDocumentIndexer(
        IGedMappingProfileStore profileStore,
        IAxisCatalog axisCatalog,
        IEntityCatalog entityCatalog,
        IGedIndexUnitOfWorkFactory unitOfWorkFactory,
        ILogger<GedDocumentIndexer> logger)
    {
        _profileStore = profileStore;
        _axisCatalog = axisCatalog;
        _entityCatalog = entityCatalog;
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
    }

    public async Task<GedIndexOutcome> IndexAsync(GedIndexRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ingested = request.Ingested;
        var managedDocumentId = request.ManagedDocumentId;

        // 1) Mapping déclaratif (profil VALIDÉ du tenant). Pré-résolution des axes (impédance sync/async : le mapper est
        //    PUR/sync, IAxisCatalog est async) → catalogue en mémoire + définitions conservées pour l'écriture des liens.
        var profile = await _profileStore.GetValidatedProfileAsync(ingested.DocumentType, cancellationToken);
        var (mappingCatalog, axisDefinitions) = await ResolveAxesAsync(profile, cancellationToken);
        var result = GedMapper.Map(profile, ingested, mappingCatalog);

        if (result.IsDeferred)
        {
            return await IndexDeferredAsync(
                managedDocumentId, ingested.SourceReference, ingested.DocumentType, result.DeferReason!, request.SoftLinks, cancellationToken);
        }

        var document = result.Document!;

        // 2a) Un identifiant externe DÉCLARÉ mais VIDE (le sélecteur a atteint une valeur PRÉSENTE mais blanche —
        //     GedSelector ne filtre pas les scalaires vides, GedMapper.MapEntities n'écarte que l'absence totale) ne peut
        //     résoudre AUCUNE identité d'entité : DÉFÉRER (INV-GED-05, motif français), jamais lever → dead-letter invisible.
        foreach (var entity in document.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.ExternalId))
            {
                var reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Le document « {ingested.SourceReference} » déclare une entité de type « {entity.EntityType} » sans identifiant externe (valeur vide) : document rangé en attente (deferred). Action opérateur : corriger le profil de mapping ou la donnée source.");
                return await IndexDeferredAsync(managedDocumentId, ingested.SourceReference, ingested.DocumentType, reason, request.SoftLinks, cancellationToken);
            }
        }

        foreach (var relation in document.Relations)
        {
            if (string.IsNullOrWhiteSpace(relation.TargetExternalId))
            {
                var reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Le document « {ingested.SourceReference} » déclare une relation « {relation.Kind} » sans identifiant de cible (valeur vide) : document rangé en attente (deferred). Action opérateur : corriger le profil de mapping ou la donnée source.");
                return await IndexDeferredAsync(managedDocumentId, ingested.SourceReference, ingested.DocumentType, reason, request.SoftLinks, cancellationToken);
            }
        }

        // 2b) Résolution des TYPES d'entité déclarés (§4.4) AVANT toute écriture : un type inconnu/inactif du catalogue
        //    DÉFÈRE le document (jamais deviner, règle 2/n°3), plutôt qu'une indexation partielle silencieuse.
        var entityTypes = new Dictionary<string, EntityTypeDefinition>(StringComparer.Ordinal);
        foreach (var code in document.Entities.Select(e => e.EntityType)
                     .Concat(document.Relations.Select(r => r.TargetType))
                     .Distinct(StringComparer.Ordinal))
        {
            var definition = await _entityCatalog.ResolveAsync(code, cancellationToken);
            if (definition is null || !definition.IsActive)
            {
                var reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Le document « {ingested.SourceReference} » réfère un type d'entité « {code} » inconnu ou inactif du catalogue : document rangé en attente (deferred). Action opérateur : déclarer/activer le type d'entité en console.");
                return await IndexDeferredAsync(managedDocumentId, ingested.SourceReference, ingested.DocumentType, reason, request.SoftLinks, cancellationToken);
            }

            entityTypes[code] = definition;
        }

        // 3) Écriture idempotente/concurrente en base TENANT (UNE transaction).
        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        var currentStatus = await unitOfWork.BeginDocumentIndexingAsync(managedDocumentId, cancellationToken);

        // Reprise CIBLÉE d'un backfill déféré (GDF10) : un document rangé `deferred` (type sans profil validé au 1er
        // passage) qui MAPPE désormais est PROMU deferred→indexed — mais SEULEMENT sur le canal backfill
        // (request.ResumeDeferred) et JAMAIS un statut terminal `indexed` (idempotence conservée : un indexed reste
        // no-op au replay). Le canal d'ingestion GED05b garde sa sémantique de replay INCHANGÉE (tout statut existant =
        // no-op, RL-04). NB : le chemin déféré (result.IsDeferred, plus haut) a déjà rendu AlreadyPresent si le re-map
        // défère encore → DEFER reste DEFER.
        var isDeferredResume = request.ResumeDeferred
            && string.Equals(currentStatus, "deferred", StringComparison.Ordinal);
        if (currentStatus is not null && !isDeferredResume)
        {
            // Replay / re-backfill (déjà indexed, ou deferred hors reprise) : no-op. Le dispose de l'UoW rend le verrou.
            LogAlreadyIndexed(_logger, managedDocumentId, currentStatus);
            return GedIndexOutcome.AlreadyPresent;
        }

        if (isDeferredResume)
        {
            // Reprise : seul le STATUT est muté (deferred→indexed), TRACÉ dans managed_document_change_log (append-only) ;
            // title/doc_kind/soft-links restent tels qu'écrits au déférement (même entrée de coffre immuable), non réécrits.
            await unitOfWork.PromoteDeferredToIndexedAsync(managedDocumentId, cancellationToken);
        }
        else
        {
            var links = request.SoftLinks;
            await unitOfWork.UpsertManagedDocumentAsync(
                new ManagedDocument(
                    managedDocumentId,
                    ingested.SourceReference,
                    ingested.DocumentType,
                    status: "indexed",
                    fiscalDocumentId: links?.FiscalDocumentId,
                    archiveEntryId: links?.ArchiveEntryId,
                    archivePath: links?.ArchivePath,
                    contentHash: links?.ContentHash),
                cancellationToken);
        }

        foreach (var axisValue in document.Axes)
        {
            var definition = axisDefinitions[axisValue.AxisCode];
            var link = new DocumentAxisLink(managedDocumentId, definition.Id, axisValue.Value, request.Source);
            await unitOfWork.AppendAxisLinkAsync(link, isSingleValued: !definition.IsMultiValue, cancellationToken);
        }

        // Entité DÉCLARÉE : résolue/créée dans le registre puis RATTACHÉE au document dans le rôle = son type
        // (le mapper ne porte pas de rôle explicite pour une entité ; le type est le rôle de rattachement — F19 §4.5).
        foreach (var entity in document.Entities)
        {
            var definition = entityTypes[entity.EntityType];
            var display = string.IsNullOrWhiteSpace(entity.Display) ? entity.ExternalId : entity.Display;
            var entityId = await unitOfWork.ResolveOrCreateEntityAsync(
                definition.Id, IdentityValue(definition, entity.ExternalId), display, request.Source, cancellationToken);
            await unitOfWork.AppendDocumentEntityLinkAsync(
                new DocumentEntityLink(managedDocumentId, entityId, role: entity.EntityType, request.Source), cancellationToken);
        }

        // Relation DÉCLARÉE (document→entité cible) : la cible est résolue/créée, le lien porte le rôle = la NATURE
        // déclarée par le profil (Kind), jamais une valeur inventée (F19 §4.5).
        foreach (var relation in document.Relations)
        {
            var definition = entityTypes[relation.TargetType];
            var entityId = await unitOfWork.ResolveOrCreateEntityAsync(
                definition.Id, IdentityValue(definition, relation.TargetExternalId), relation.TargetExternalId, request.Source, cancellationToken);
            await unitOfWork.AppendDocumentEntityLinkAsync(
                new DocumentEntityLink(managedDocumentId, entityId, role: relation.Kind, request.Source), cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        LogIndexed(_logger, managedDocumentId, document.Axes.Count, document.Entities.Count + document.Relations.Count);
        return GedIndexOutcome.Indexed;
    }

    public async Task<GedIndexOutcome> IndexDeferredAsync(
        Guid managedDocumentId,
        string sourceReference,
        string? docKind,
        string deferReason,
        GedDocumentSoftLinks? softLinks = null,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        var currentStatus = await unitOfWork.BeginDocumentIndexingAsync(managedDocumentId, cancellationToken);
        if (currentStatus is not null)
        {
            LogAlreadyIndexed(_logger, managedDocumentId, currentStatus);
            return GedIndexOutcome.AlreadyPresent;
        }

        await unitOfWork.UpsertManagedDocumentAsync(
            new ManagedDocument(
                managedDocumentId,
                sourceReference,
                docKind,
                status: "deferred",
                deferReason: deferReason,
                fiscalDocumentId: softLinks?.FiscalDocumentId,
                archiveEntryId: softLinks?.ArchiveEntryId,
                archivePath: softLinks?.ArchivePath,
                contentHash: softLinks?.ContentHash),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        LogDeferred(_logger, managedDocumentId, deferReason);
        return GedIndexOutcome.Deferred;
    }

    // §4.4 — clé d'identité de l'entité : normalisée (NFC + trim, comme une valeur de texte libre — jamais deviner) si
    // le type déclare une clé de résolution ; null sinon (pas de déduplication auto, création par observation).
    private static string? IdentityValue(EntityTypeDefinition definition, string externalId) =>
        definition.IdentityKey is null ? null : CanonicalJsonWriter.NormalizeToNfc(externalId).Trim();

    [LoggerMessage(EventId = 7310, Level = LogLevel.Information,
        Message = "Indexation GED ignorée pour le document {ManagedDocumentId} : statut {Status} (déjà indexé/déféré — idempotent, RL-04).")]
    private static partial void LogAlreadyIndexed(ILogger logger, Guid managedDocumentId, string status);

    [LoggerMessage(EventId = 7313, Level = LogLevel.Information,
        Message = "Indexation GED : document {ManagedDocumentId} indexé ({AxisCount} axe(s), {EntityLinkCount} lien(s) d'entité).")]
    private static partial void LogIndexed(ILogger logger, Guid managedDocumentId, int axisCount, int entityLinkCount);

    [LoggerMessage(EventId = 7314, Level = LogLevel.Information,
        Message = "Indexation GED : document {ManagedDocumentId} rangé en attente (deferred) — {Reason}")]
    private static partial void LogDeferred(ILogger logger, Guid managedDocumentId, string reason);

    private async Task<(IAxisMappingCatalog Catalog, Dictionary<string, AxisDefinition> Definitions)> ResolveAxesAsync(
        GedMappingProfile? profile,
        CancellationToken cancellationToken)
    {
        var targets = new Dictionary<string, AxisMappingTarget>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, AxisDefinition>(StringComparer.Ordinal);
        if (profile is null)
        {
            return (new DictionaryAxisMappingCatalog(targets), definitions);
        }

        foreach (var code in profile.AxisRules.Select(r => r.AxisCode).Distinct(StringComparer.Ordinal))
        {
            var definition = await _axisCatalog.ResolveAsync(code, cancellationToken);

            // Un axe inconnu OU inactif reste ABSENT du catalogue de mapping → le mapper rend null puis DÉFÈRE
            // (contrat IAxisMappingCatalog « inconnu ou inactif → null », jamais deviner).
            if (definition is null || !definition.IsActive)
            {
                continue;
            }

            targets[code] = new AxisMappingTarget(definition.Code, definition.DataType, definition.ValueScale, definition.IsMultiValue);
            definitions[code] = definition;
        }

        return (new DictionaryAxisMappingCatalog(targets), definitions);
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
