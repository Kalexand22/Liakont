namespace Liakont.Host.Ged;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition en LECTURE de l'exploration de graphe du portail GED (F19 §6.7) : isole la traversée bornée
/// bidirectionnelle (GED08), la résolution du droit de confidentialité (GED06) et l'audit de consultation (GED13)
/// HORS de la page Blazor — la page reste présentationnelle (CLAUDE.md n°19) et testable. Tenant-scopée par la
/// connexion de l'index (CLAUDE.md n°9).
/// </summary>
internal interface IGedGraphQueries
{
    /// <summary>
    /// Explore le graphe d'entités depuis une racine et retourne UNE page de documents atteignables, paginée par
    /// KEYSET (RL-20, INV-GED-09). Le droit <c>liakont.ged.confidential</c> est résolu SERVER-SIDE (la traversée
    /// exclut alors racine + voisins confidentiels, §6.4/§6.5, jamais sur la foi d'un booléen fourni par l'appelant)
    /// et l'opération est tracée au journal de consultation (<c>explore_entity</c>, §6.6).
    /// </summary>
    Task<GedGraphResults> ExploreAsync(GedGraphRequest request, CancellationToken cancellationToken = default);
}
