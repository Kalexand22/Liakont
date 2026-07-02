namespace Liakont.Host.Ged;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Lecture d'assemblage de la fiche document GED (GED09b, F19 §6.7). INTERNE au Host (aucune exposition
/// publique) : orchestre le port de lecture GED (<c>IGedDocumentQueries</c>, masquage confidentiel server-side)
/// + la surface de coffre (<c>Archive.Contracts</c> : intégrité re-lue + aperçu) + le journal de consultation
/// (<c>view_document</c>), et calcule le droit confidentiel de l'acteur. La page reste mince (aucune logique
/// métier, aucun accès base direct).
/// </summary>
internal interface IGedDocumentConsoleQueries
{
    /// <summary>
    /// Assemble la fiche du document <paramref name="managedDocumentId"/>, ou <see langword="null"/> s'il
    /// n'existe pas dans le tenant courant. Journalise une consultation <c>view_document</c> (best-effort ; en
    /// régime probant, une trace en échec LÈVE — fail-closed §6.6).
    /// </summary>
    Task<GedDocumentDetailViewModel?> GetAsync(Guid managedDocumentId, CancellationToken cancellationToken = default);
}
