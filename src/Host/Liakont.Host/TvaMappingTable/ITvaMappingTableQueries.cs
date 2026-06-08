namespace Liakont.Host.TvaMappingTable;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de la page « Paramétrage comptable — Table TVA » (WEB07a) : assemble la table de
/// mapping du tenant courant + son journal (lecture API04) et déclenche la validation humaine de la
/// table (workflow expert-comptable). Isole l'assemblage et la commande hors de la page Blazor (la
/// page reste présentationnelle, CLAUDE.md n°19) et les rend testables unitairement. Tenant-scopée
/// (CLAUDE.md n°9).
/// </summary>
internal interface ITvaMappingTableQueries
{
    /// <summary>Assemble le modèle de la page (table + journal + identité opérateur) pour le tenant courant.</summary>
    Task<TvaMappingTableViewModel> GetTableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marque la table de mapping TVA du tenant courant comme VALIDÉE (lève la suspension des envois,
    /// garde-fou PIP01). Le valideur enregistré est l'identité AUTHENTIFIÉE de l'opérateur — jamais une
    /// valeur fournie par l'appelant (un opérateur ne peut pas signer au nom d'un autre ; parité avec
    /// l'endpoint API04 <c>POST /settings/tva-mapping/validate</c>). La validation est journalisée
    /// (append-only) comme toute mutation.
    /// </summary>
    Task ValidateAsync(CancellationToken cancellationToken = default);
}
