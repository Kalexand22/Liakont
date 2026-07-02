namespace Liakont.Host.Navigation;

using System;
using System.Linq;
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
    // Liakont (recette 2026-07-01, décision Karl) : entrée de menu retirée de la section Audit — voir GetSection.
    private const string AuditPoliciesHref = "/admin/audit/policies";

    private readonly IPermissionService _permissions;

    public AuditNavVisibilityFilter(IPermissionService permissions) => _permissions = permissions;

    public NavSection GetSection()
    {
        var section = new AuditNavSectionProvider().GetSection();

        // Sans la permission socle audit.trail.view, la section entière est vidée → omise par BuildNavTree
        // (NavNodeAdapter filtre les sections à 0 item) : plus d'entrée morte vers des pages vides.
        if (!_permissions.HasPermission(AuditPermissions.AuditView))
        {
            return section with { Items = [] };
        }

        // Liakont (recette 2026-07-01, décision Karl) : l'entrée « Politiques » (/admin/audit/policies) est
        // masquée de la nav. Cet écran socle configure l'audit GÉNÉRIQUE (activer/désactiver le tracking par
        // entité), sans valeur produit aujourd'hui — et un bouton « Désactiver l'audit » détonne sur un produit
        // de conformité. Seule l'entrée de MENU disparaît ; la ROUTE reste ouverte au super-admin (« Journal
        // d'audit » demeure). Socle NON modifié (CLAUDE.md n°11) : seule la VISIBILITÉ est décidée ici.
        var items = section.Items
            .Where(item => !string.Equals(item.Href, AuditPoliciesHref, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return section with { Items = items };
    }
}
