namespace Liakont.Host.Navigation;

using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Audit.Web;

/// <summary>
/// Filtre de visibilité Liakont pour la section de navigation « Audit » du socle vendored (FIX303).
/// Le <see cref="AuditNavSectionProvider"/> du socle déclare « Journal d'audit » (/admin/audit) et
/// « Politiques » (/admin/audit/policies) INCONDITIONNELLEMENT, mais les deux pages socle cibles sont
/// gardées par la permission socle <see cref="AuditPermissions.AuditView"/> (<c>audit.trail.view</c>) —
/// JAMAIS accordée par un rôle Liakont (matrice §3 de <c>identity-permissions-liakont.md</c>, matérialisée
/// par le <c>RolePermissionCatalog</c> immuable : un rôle Liakont n'accorde que read/actions/settings/supervision) :
/// seul un super-admin (Admin / SystemAdmin / stratum-admin) peut les ouvrir. Sans ce filtre, la section
/// menait à des pages entièrement VIDES pour tout opérateur normal (bug remonté en recette GATE_CONSOLE_WEB
/// du 2026-06-11 — même cause que l'assainissement FIX209 de la nav socle).
/// </summary>
/// <remarks>
/// La section n'est exposée que lorsque l'utilisateur courant porte réellement <see cref="AuditPermissions.AuditView"/>.
/// SCOPED car la visibilité dépend de l'utilisateur (même schéma que <see cref="JobNavVisibilityFilter"/> et
/// <see cref="NotificationNavVisibilityFilter"/>). Le socle vendored n'est PAS modifié (CLAUDE.md n°11) et AUCUNE
/// permission n'est inventée : la matrice §3 reste intacte — la consultation du journal d'audit socle est une
/// surface super-admin. La ROUTE /admin/audit (Routes.razor + discovery d'assembly dans AppBootstrap) reste
/// intacte pour le super-admin. On délègue à <see cref="AuditNavSectionProvider.GetSection"/> pour ne pas dupliquer
/// la définition socle de la section (titre, icône, ordre, libellés) : seule la VISIBILITÉ est décidée ici.
/// </remarks>
internal sealed class AuditNavVisibilityFilter : INavSectionProvider
{
    private readonly IPermissionService _permissions;

    public AuditNavVisibilityFilter(IPermissionService permissions) => _permissions = permissions;

    public NavSection GetSection()
    {
        var section = new AuditNavSectionProvider().GetSection();

        // Une section vidée de ses items est omise par BuildNavTree (NavNodeAdapter filtre les sections à
        // 0 item) : la section disparaît proprement de la sidebar au lieu de mener à des pages vides.
        return _permissions.HasPermission(AuditPermissions.AuditView)
            ? section
            : section with { Items = [] };
    }
}
