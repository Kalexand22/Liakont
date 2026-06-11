namespace Liakont.Host.Alertes;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de la page « Paramétrage › Alertes » (FIX210) : assemble le dispositif d'alerte du tenant
/// courant (règles, seuils effectifs, e-mail opérateur — via le Contract Supervision) et la saisie éditable,
/// et applique les mutations via les commandes TenantSettings (seuils, contact). Isole l'accès aux modules
/// hors de la page Blazor (la page reste présentationnelle — CLAUDE.md n°19) et rend l'ensemble testable.
/// Tenant-scopé : la société est résolue côté handler (jamais un paramètre client — CLAUDE.md n°9).
/// </summary>
internal interface IAlertesConsoleService
{
    /// <summary>Assemble le dispositif d'alerte du tenant courant et la saisie pré-remplie.</summary>
    Task<AlertesViewModel> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre les seuils des règles ACTIVES + l'activation du contact (<c>SetAlertThresholdsCommand</c>).
    /// Les seuils des règles gelées sont préservés (relus puis réémis tels quels — l'éditeur ne les expose pas).
    /// </summary>
    Task SaveThresholdsAsync(AlertesThresholdInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre le SEUL e-mail de contact d'alerte (<c>SetAlertContactEmailCommand</c>). Le profil doit
    /// exister (sinon <see cref="Stratum.Common.Abstractions.Exceptions.NotFoundException"/>, message porté par la page).
    /// </summary>
    Task SaveContactAsync(string? contactEmailAlerte, CancellationToken cancellationToken = default);
}
