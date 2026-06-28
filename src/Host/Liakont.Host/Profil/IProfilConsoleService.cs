namespace Liakont.Host.Profil;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de l'écran « Paramétrage › Profil légal » (BUG-15) : lit le profil du tenant courant
/// (<c>GetTenantProfileQuery</c>) et applique la modification de la raison sociale, de l'adresse et du contact
/// d'alerte via <c>SaveTenantProfileCommand</c> (le handler valide et journalise — append-only). Le SIREN est
/// immuable (INV-TENANTSETTINGS-001) : il est affiché en lecture seule et repassé INCHANGÉ depuis le profil
/// persisté (jamais depuis le client) — ce chemin ne peut donc pas le modifier. Tenant-scopé : la société est
/// résolue côté handler (CLAUDE.md n°9). Isole l'accès au module hors de la page Blazor (CLAUDE.md n°19).
/// </summary>
internal interface IProfilConsoleService
{
    /// <summary>
    /// Assemble le profil légal du tenant courant, pré-rempli, SIREN en lecture seule. Renvoie <c>null</c>
    /// si aucun profil n'a encore été créé pour le tenant (rien à éditer).
    /// </summary>
    Task<ProfilViewModel?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre la raison sociale, l'adresse et le contact d'alerte du profil (<c>SaveTenantProfileCommand</c>).
    /// Le SIREN est repris du profil persisté (immuable) — un changement de SIREN est impossible par ce chemin.
    /// La validation (adresse, code pays, e-mail) reste du ressort du handler / domaine ; un invariant violé
    /// remonte une exception (message porté par la page).
    /// </summary>
    Task SaveAsync(ProfilInput input, CancellationToken cancellationToken = default);
}
