namespace Liakont.Host.Navigation;

using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Web;

/// <summary>
/// Filtre de visibilité Liakont pour la section de navigation « Jobs » du socle vendored (FIX07c).
/// Le <see cref="JobNavSectionProvider"/> du socle déclare l'entrée « Planifications » (/admin/jobs)
/// INCONDITIONNELLEMENT, mais la page socle cible est gardée par la permission socle
/// <see cref="JobPermissions.View"/> — JAMAIS accordée par un rôle Liakont (matrice §3 de
/// <c>identity-permissions-liakont.md</c>, matérialisée par le <c>RolePermissionCatalog</c> immuable) :
/// seul un super-admin (Admin / SystemAdmin / stratum-admin) peut l'ouvrir. Sans ce filtre, l'entrée mène
/// à une page entièrement VIDE pour tout opérateur normal (bug remonté en recette GATE_CONSOLE_WEB du
/// 2026-06-11).
/// </summary>
/// <remarks>
/// La section n'est exposée que lorsque l'utilisateur courant porte réellement <see cref="JobPermissions.View"/>.
/// SCOPED car la visibilité dépend de l'utilisateur (même schéma que <see cref="LiakontNavSectionProvider"/>).
/// Le socle vendored n'est PAS modifié (CLAUDE.md n°11) et AUCUNE permission n'est inventée : la matrice §3
/// reste intacte — la gestion des planifications « relève du déploiement » (ADR-0011), surface super-admin.
/// On délègue à <see cref="JobNavSectionProvider.GetSection"/> pour ne pas dupliquer la définition socle de
/// la section (titre, icône, ordre, libellé) : seule la VISIBILITÉ est décidée ici.
/// </remarks>
internal sealed class JobNavVisibilityFilter : INavSectionProvider
{
    private readonly IPermissionService _permissions;

    public JobNavVisibilityFilter(IPermissionService permissions) => _permissions = permissions;

    public NavSection GetSection()
    {
        var section = new JobNavSectionProvider().GetSection();

        // Une section vidée de ses items est omise par BuildNavTree (NavNodeAdapter filtre les sections à
        // 0 item) : l'entrée disparaît proprement de la sidebar au lieu de mener à une page vide.
        return _permissions.HasPermission(JobPermissions.View)
            ? section
            : section with { Items = [] };
    }
}
