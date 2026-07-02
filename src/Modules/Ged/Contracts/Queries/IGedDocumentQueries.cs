namespace Liakont.Modules.Ged.Contracts.Queries;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Port de LECTURE d'un document géré pour la fiche <c>/ged/document/{id}</c> (F19 §6.7, GED09b). Tenant-scopé
/// par la connexion (isolation = la connexion, INV-GED-08 ; un document d'un autre tenant est invisible).
/// Le masquage de confidentialité (§6.5) est MATÉRIALISÉ server-side dans le SQL (RL-31, anti-oracle) : le
/// paramètre <paramref name="hasConfidentialRight"/> — calculé par l'appelant depuis <c>liakont.ged.confidential</c>,
/// comme <c>@hasConfidentialRight</c> des requêtes de recherche/graphe — décide de l'inclusion des axes/entités
/// confidentiels. La page n'ajoute AUCUN filtrage de confidentialité (le serveur l'a déjà appliqué).
/// </summary>
public interface IGedDocumentQueries
{
    /// <summary>
    /// Restitue la fiche du document <paramref name="managedDocumentId"/> (méta + axes + entités courants), ou
    /// <see langword="null"/> s'il n'existe pas dans le tenant courant. Les axes/entités confidentiels sont
    /// exclus lorsque <paramref name="hasConfidentialRight"/> est <see langword="false"/>.
    /// </summary>
    Task<GedManagedDocumentView?> GetAsync(
        Guid managedDocumentId,
        bool hasConfidentialRight,
        CancellationToken cancellationToken = default);
}
