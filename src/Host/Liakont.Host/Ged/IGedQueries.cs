namespace Liakont.Host.Ged;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition en LECTURE du portail GED (F19 §6.7) : isole l'accès à l'index de recherche (GED08), la résolution
/// du droit de confidentialité (GED06) et l'audit de consultation (GED13) HORS de la page Blazor — la page reste
/// présentationnelle (CLAUDE.md n°19) et testable. Tenant-scopée par la connexion de l'index (CLAUDE.md n°9).
/// </summary>
internal interface IGedQueries
{
    /// <summary>
    /// Exécute une page de recherche documentaire : recherche multi-axes + plein texte + facettes, paginée par
    /// KEYSET (RL-20). Le droit <c>liakont.ged.confidential</c> est résolu SERVER-SIDE (le masquage §6.5 ne dépend
    /// jamais d'un booléen fourni par l'appelant) et l'opération est tracée au journal de consultation (§6.6).
    /// </summary>
    Task<GedSearchResults> SearchAsync(GedSearchRequest request, CancellationToken cancellationToken = default);
}
