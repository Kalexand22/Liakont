namespace Liakont.Host.Dashboard;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition en LECTURE du tableau de bord d'accueil (WEB01) : assemble, pour le tenant courant, les
/// compteurs de documents par état, l'état des agents, l'état de la table TVA et la cadence déclarative.
/// Isole l'assemblage hors de la page Blazor (la page reste présentationnelle, CLAUDE.md n°19) et le rend
/// testable unitairement. Tenant-scopée (CLAUDE.md n°9).
/// </summary>
internal interface IDashboardQueries
{
    /// <summary>Assemble le modèle du tableau de bord pour le tenant courant.</summary>
    Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default);
}
