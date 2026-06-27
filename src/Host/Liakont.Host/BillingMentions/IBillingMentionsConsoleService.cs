namespace Liakont.Host.BillingMentions;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de l'écran « Paramétrage › Mentions de facturation » (BUG-26, F12-A §3.4) : assemble les
/// mentions de facturation du tenant courant (lecture via <c>GetBillingMentionsQuery</c>) et la saisie
/// pré-remplie, et applique la modification via la commande <c>SetBillingMentionsCommand</c> (qui journalise).
/// Isole l'accès au module hors de la page Blazor (la page reste présentationnelle — CLAUDE.md n°19).
/// Tenant-scopé : la société est résolue côté handler (jamais un paramètre client — CLAUDE.md n°9).
/// </summary>
internal interface IBillingMentionsConsoleService
{
    /// <summary>Assemble les mentions de facturation du tenant courant, pré-remplies (ou « non renseigné »).</summary>
    Task<BillingMentionsViewModel> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre les mentions de facturation (<c>SetBillingMentionsCommand</c>). Normalise les chaînes
    /// vides en <c>null</c> (« non renseigné ») ; aucun contenu n'est inventé (CLAUDE.md n°2/7).
    /// </summary>
    Task SaveAsync(BillingMentionsInput input, CancellationToken cancellationToken = default);
}
