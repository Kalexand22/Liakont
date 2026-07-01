namespace Liakont.Modules.Ged.Contracts.Backfill;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Point d'entrée d'indexation DIRECT (hors-outbox) du backfill rétroactif GED (GED10, F19 §11 D12). Surface PUBLIQUE
/// de la GED consommée par l'orchestrateur de backfill côté Host : la GED reçoit une projection plate d'un document
/// fiscal archivé et l'indexe (ou le DÉFÈRE) de façon IDEMPOTENTE — sans jamais référencer les modules fiscaux
/// (la GED est un silo, frontière F19 §7). Chemin DIRECT : ce n'est PAS un effet de bord du flux fiscal (RL-21).
/// </summary>
/// <remarks>
/// <b>Portée d'un re-passage (idempotence TERMINALE, RL-04).</b> L'idempotence est terminale sur TOUT statut existant
/// (<c>indexed</c> comme <c>deferred</c>) : un re-passage n'indexe QUE les entrées de coffre PAS ENCORE présentes dans
/// l'index GED. Un document déjà <c>deferred</c> (typiquement les types FISCAUX « facture »/« avoir » qui n'ont pas de
/// profil de mapping GED — le DÉFÉREMENT est ici le cas NOMINAL, jamais deviner) n'est PAS re-mappé par un re-run, même
/// si un profil est créé APRÈS coup (sémantique cohérente avec le replay du consommateur GED05b). La <b>reprise des
/// <c>deferred</c></b> (re-mapping après création d'un profil) est une capacité DISTINCTE, hors périmètre V1 (fast-follow) :
/// un opérateur qui ajoute un profil et veut ré-indexer un corpus déjà déféré ne peut PAS s'en remettre à un simple re-run.
/// </remarks>
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
