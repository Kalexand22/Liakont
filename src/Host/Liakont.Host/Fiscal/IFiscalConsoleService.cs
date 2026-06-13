namespace Liakont.Host.Fiscal;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de l'écran « Paramétrage › Fiscal » (FIX301) : assemble le paramétrage fiscal du tenant courant
/// (lecture via <c>GetFiscalSettingsQuery</c>) et la saisie pré-remplie, et applique la modification via la
/// commande <c>SetFiscalSettingsCommand</c> (qui journalise et valide). Isole l'accès au module hors de la page
/// Blazor (la page reste présentationnelle — CLAUDE.md n°19). Tenant-scopé : la société est résolue côté
/// handler (jamais un paramètre client — CLAUDE.md n°9).
/// </summary>
internal interface IFiscalConsoleService
{
    /// <summary>Assemble le paramétrage fiscal du tenant courant, pré-rempli, avec les listes fermées admises.</summary>
    Task<FiscalViewModel> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre le paramétrage fiscal (<c>SetFiscalSettingsCommand</c>). Convertit le jeton tri-état
    /// <c>VatOnDebits</c> en <c>bool?</c> et normalise les chaînes vides en <c>null</c> (« non renseigné » =
    /// suspension conservée). Une valeur inconnue est REJETÉE par le handler (<see cref="System.ArgumentException"/>,
    /// message porté par la page) — jamais devinée.
    /// </summary>
    Task SaveAsync(FiscalSettingsInput input, CancellationToken cancellationToken = default);
}
