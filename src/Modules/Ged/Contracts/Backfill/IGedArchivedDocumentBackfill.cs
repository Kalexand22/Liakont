namespace Liakont.Modules.Ged.Contracts.Backfill;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Point d'entrée d'indexation DIRECT (hors-outbox) du backfill rétroactif GED (GED10, F19 §11 D12). Surface PUBLIQUE
/// de la GED consommée par l'orchestrateur de backfill côté Host : la GED reçoit une projection plate d'un document
/// fiscal archivé et l'indexe (ou le DÉFÈRE) de façon IDEMPOTENTE — sans jamais référencer les modules fiscaux
/// (la GED est un silo, frontière F19 §7). Chemin DIRECT : ce n'est PAS un effet de bord du flux fiscal (RL-21).
/// </summary>
public interface IGedArchivedDocumentBackfill
{
    /// <summary>
    /// Indexe rétroactivement un document du corpus fiscal déjà scellé. Idempotent (clé =
    /// <see cref="GedBackfillDocumentRequest.ArchiveEntryId"/>) : un re-passage rend <see cref="GedBackfillOutcome.AlreadyPresent"/>
    /// sans réécriture. Un document dont le type n'a pas de profil validé est <see cref="GedBackfillOutcome.Deferred"/>
    /// (jamais deviné, règle 3).
    /// </summary>
    Task<GedBackfillOutcome> BackfillAsync(GedBackfillDocumentRequest request, CancellationToken cancellationToken = default);
}
