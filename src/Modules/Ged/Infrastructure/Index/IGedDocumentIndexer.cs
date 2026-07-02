namespace Liakont.Modules.Ged.Infrastructure.Index;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Foyer UNIQUE d'écriture de l'index GED (F19 §3.4/§4.5). Encapsule la séquence « charger le profil VALIDÉ →
/// <c>GedMapper.Map</c> → valider les types d'entité → écrire (statut <c>indexed</c>/<c>deferred</c>) sous garde de
/// concurrence par document (RL-02/RL-04) ». UN seul chemin d'écriture existe (invariant « exactement un chemin de
/// statut ») : le consommateur d'ingestion GED (GED05b) ET le backfill rétroactif du corpus fiscal (GED10) l'appellent
/// tous deux — jamais de logique d'indexation dupliquée qui pourrait diverger. DEFER PLUTÔT QUE DEVINER (INV-GED-05,
/// règle 3) ; montants d'axe <c>number</c> en <c>decimal</c> half-up (règle 1, via <c>ValueNormalizer</c>).
/// </summary>
internal interface IGedDocumentIndexer
{
    /// <summary>
    /// Mappe puis indexe le document. Idempotent : un document déjà <c>indexed</c>/<c>deferred</c> rend
    /// <see cref="GedIndexOutcome.AlreadyPresent"/> sans réécriture (garde <c>pg_advisory_xact_lock</c> + statut lu).
    /// Un document non mappable (profil absent, axe requis non résolu, type d'entité inconnu, identifiant vide) est
    /// rangé <c>deferred</c> avec un motif français actionnable.
    /// </summary>
    Task<GedIndexOutcome> IndexAsync(GedIndexRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Range DIRECTEMENT un document en <c>deferred</c> (sans mapping) — pour les cas où l'appelant ne dispose PAS
    /// d'un pivot exploitable (ex. contenu stagé altéré côté consommateur). Idempotent, même garde de concurrence.
    /// </summary>
    Task<GedIndexOutcome> IndexDeferredAsync(
        Guid managedDocumentId,
        string sourceReference,
        string? docKind,
        string deferReason,
        GedDocumentSoftLinks? softLinks = null,
        CancellationToken cancellationToken = default);
}
