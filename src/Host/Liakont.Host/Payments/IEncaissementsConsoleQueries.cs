namespace Liakont.Host.Payments;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition en lecture de la page Encaissements (WEB06) : assemble les agrégats jour×taux de
/// l'e-reporting de paiement (PIP03a, <c>GET /payments</c>) et l'état du paramétrage du tenant pertinent
/// pour l'affichage (capacité de la PA, complétude fiscale — <c>GET /settings</c>). Isole l'assemblage hors
/// de la page (présentationnelle). Tenant-scopé par construction (les lectures sous-jacentes s'exécutent sur
/// la base du tenant courant — CLAUDE.md n°9/17). AUCUNE logique métier ni fiscale : la qualification des
/// agrégats vient de PIP03a et est seulement reportée (CLAUDE.md n°2).
/// </summary>
internal interface IEncaissementsConsoleQueries
{
    /// <summary>
    /// Assemble la vue Encaissements du tenant courant pour la période donnée (<c>"yyyy-MM"</c> ; vide/null =
    /// pas de filtre de date). Ne lève pas pour un tenant non (entièrement) paramétré : agrégats vides + état
    /// de paramétrage neutre.
    /// </summary>
    Task<EncaissementsViewModel> GetEncaissementsAsync(string? period, CancellationToken cancellationToken = default);
}
