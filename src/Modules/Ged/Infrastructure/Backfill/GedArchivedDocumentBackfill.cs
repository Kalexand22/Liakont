namespace Liakont.Modules.Ged.Infrastructure.Backfill;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Contracts.Backfill;
using Liakont.Modules.Ged.Infrastructure.Index;

/// <summary>
/// Point d'entrée d'indexation du backfill rétroactif GED (GED10, F19 §11 D12), tenant-scopé par ses dépendances.
/// Projette une entrée du corpus fiscal déjà scellé (<see cref="GedBackfillDocumentRequest"/>) en pivot GED BRUT et
/// délègue au foyer d'écriture UNIQUE <see cref="IGedDocumentIndexer"/> — le même chemin que l'ingestion GED (GED05b),
/// avec la source <c>import</c> et les soft-links posés. Idempotent : l'identité GED est DÉTERMINISTE
/// (<see cref="GedDeterministicId.ForArchiveEntry"/>), donc un re-passage sur un document déjà indexé no-ope (RL-21) ;
/// un document déféré devenu mappable (profil validé depuis) est REPRIS et promu deferred→indexed (GDF10, ResumeDeferred).
/// Chemin DIRECT (hors-outbox) : le mapping <c>deferred</c> pour un type sans profil est le cas nominal (jamais deviner, règle 3).
/// </summary>
internal sealed class GedArchivedDocumentBackfill : IGedArchivedDocumentBackfill
{
    /// <summary>Provenance des liens écrits par le backfill (<c>ck_dal_source</c>) — import d'un corpus existant.</summary>
    private const string BackfillSource = "import";

    private readonly IGedDocumentIndexer _indexer;

    public GedArchivedDocumentBackfill(IGedDocumentIndexer indexer) => _indexer = indexer;

    public async Task<GedBackfillOutcome> BackfillAsync(GedBackfillDocumentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var managedDocumentId = GedDeterministicId.ForArchiveEntry(request.ArchiveEntryId);

        var ingested = new IngestedDocumentDto(
            sourceReference: request.SourceReference,
            documentType: request.DocumentType,
            sourceTimestampUtc: request.SourceTimestampUtc,
            content: null,
            sourceFields: request.SourceFields);

        var softLinks = new GedDocumentSoftLinks(
            FiscalDocumentId: request.FiscalDocumentId,
            ArchiveEntryId: request.ArchiveEntryId,
            ArchivePath: request.ArchivePath,
            ContentHash: request.ContentHash);

        // ResumeDeferred: le job Host re-énumère TOUT le corpus à chaque run (idempotent) → un re-run REPREND les
        // documents déférés devenus mappables (profil créé+validé depuis) en les promouvant deferred→indexed (GDF10).
        // Un document déjà indexé reste no-op ; un déféré encore non mappable reste déféré.
        var outcome = await _indexer.IndexAsync(
            new GedIndexRequest(managedDocumentId, ingested, BackfillSource, softLinks, ResumeDeferred: true), cancellationToken);

        return outcome switch
        {
            GedIndexOutcome.Indexed => GedBackfillOutcome.Indexed,
            GedIndexOutcome.Deferred => GedBackfillOutcome.Deferred,
            _ => GedBackfillOutcome.AlreadyPresent,
        };
    }
}
