namespace Liakont.Host.TvaMappingTable;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de la page « Paramétrage comptable — Table TVA » (WEB07a lecture + WEB07b édition) :
/// assemble la table de mapping du tenant courant + son journal + le rapport de couverture + les listes
/// fermées d'édition (lecture API04/TVA03/TVA05), déclenche la validation humaine de la table, et
/// applique les mutations de règles (ajout / modification / suppression) via les commandes TVA05.
/// Isole l'assemblage et les commandes hors de la page Blazor (la page reste présentationnelle,
/// CLAUDE.md n°19) et les rend testables unitairement. Tenant-scopée (CLAUDE.md n°9).
/// </summary>
internal interface ITvaMappingTableQueries
{
    /// <summary>
    /// Assemble le modèle de la page (table + journal + identité opérateur + couverture + listes
    /// fermées d'édition) pour le tenant courant.
    /// </summary>
    Task<TvaMappingTableViewModel> GetTableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marque la table de mapping TVA du tenant courant comme VALIDÉE (lève la suspension des envois,
    /// garde-fou PIP01). Le valideur enregistré est l'identité AUTHENTIFIÉE de l'opérateur — jamais une
    /// valeur fournie par l'appelant (un opérateur ne peut pas signer au nom d'un autre ; parité avec
    /// l'endpoint API04 <c>POST /settings/tva-mapping/validate</c>). La validation est journalisée
    /// (append-only) comme toute mutation.
    /// </summary>
    Task ValidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute une règle à la table du tenant courant (commande TVA05 <c>AddMappingRuleCommand</c>).
    /// Toute mutation repasse la table « NON VALIDÉE » et est journalisée (append-only) côté handler.
    /// La validation structurelle (catégorie E → VATEX, taux, doublon) est appliquée par le handler :
    /// une règle invalide lève (le message opérateur français est affiché par la page).
    /// </summary>
    Task AddRuleAsync(TvaRuleFormModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remplace les valeurs d'une règle existante (commande TVA05 <c>UpdateMappingRuleCommand</c>),
    /// identifiée par le couple (code régime, part) — inchangé. Mêmes garanties d'invalidation et de
    /// journalisation que l'ajout.
    /// </summary>
    Task UpdateRuleAsync(TvaRuleFormModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime une règle de la table du tenant courant (commande TVA05 <c>RemoveMappingRuleCommand</c>),
    /// identifiée par le couple (code régime, part). Mêmes garanties d'invalidation et de journalisation.
    /// </summary>
    Task RemoveRuleAsync(string sourceRegimeCode, string part, CancellationToken cancellationToken = default);
}
