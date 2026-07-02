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
/// <b>Portée d'un re-passage (idempotence + REPRISE des déférés, GDF10).</b> Un re-passage n'indexe que les entrées de
/// coffre pas encore présentes ET <b>reprend les documents déférés devenus mappables</b> : un document rangé
/// <c>deferred</c> (typiquement les types FISCAUX « facture »/« avoir » sans profil de mapping GED — le DÉFÉREMENT est ici
/// le cas NOMINAL, jamais deviner) est RE-MAPPÉ au re-run et PROMU <c>deferred</c>→<c>indexed</c> dès qu'un profil VALIDÉ
/// couvre son type. La mutation de statut est tracée (<c>managed_document_change_log</c> append-only, <c>status_changed</c>).
/// L'idempotence reste TERMINALE sur <c>indexed</c> : un document déjà indexé reste un no-op au replay ; un déféré encore
/// non mappable reste déféré (DEFER reste DEFER, jamais un rejet silencieux). L'opérateur qui ajoute un profil pour un type
/// déféré n'a donc qu'à RELANCER le backfill — l'action prescrite par le motif de déférement produit bien son effet.
/// Cette reprise est PROPRE au canal backfill (le job Host re-énumère tout le corpus) ; le canal d'ingestion GED05b garde
/// une idempotence de replay terminale sur tout statut (RL-04).
/// </remarks>
public interface IGedArchivedDocumentBackfill
{
    /// <summary>
    /// Indexe rétroactivement un document du corpus fiscal déjà scellé. Idempotent (clé =
    /// <see cref="GedBackfillDocumentRequest.ArchiveEntryId"/>) : un re-passage sur un document déjà <c>indexed</c> rend
    /// <see cref="GedBackfillOutcome.AlreadyPresent"/> sans réécriture ; un document déjà <c>deferred</c> devenu mappable
    /// (profil validé depuis) est PROMU et rend <see cref="GedBackfillOutcome.Indexed"/> (reprise ciblée GDF10, cf. remarques).
    /// Un document dont le type n'a pas de profil validé est <see cref="GedBackfillOutcome.Deferred"/> (jamais deviné, règle 3).
    /// </summary>
    Task<GedBackfillOutcome> BackfillAsync(GedBackfillDocumentRequest request, CancellationToken cancellationToken = default);
}
