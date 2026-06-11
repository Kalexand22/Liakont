namespace Liakont.Host.Navigation;

using System.Linq;
using Liakont.Host.Security;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;
using Stratum.Modules.Notification.Web;

/// <summary>
/// Filtre de visibilité Liakont pour la section de navigation « Notifications » du socle vendored (FIX209).
/// Le <see cref="NotificationNavSectionProvider"/> du socle déclare SEPT entrées (Templates, Règles de
/// routage, Webhooks, Simulation, SLA, Services, Integrations) — toutes héritées d'un produit ERP générique
/// dont la majorité n'a aucun sens pour un opérateur Liakont, et dont plusieurs (Webhooks, Integrations,
/// test-fire) lèvent <c>InvalidOperationException("No company context available.")</c> faute de contexte
/// d'entreprise sous OIDC (recette GATE_CONSOLE_WEB run 2, 2026-06-11 ; décision opérateur E5).
/// </summary>
/// <remarks>
/// On NE garde QUE « Templates » : c'est la seule page utile au produit (édition des e-mails d'alerte de
/// supervision, templates socle <c>supervision.alert.*</c> consommés par SUP03). La page liste socle
/// (<c>/admin/notifications/templates</c>) est <c>[Authorize]</c> et lit les templates GLOBAUX
/// (<c>company_id IS NULL</c>) via la connexion système — elle ne dépend donc d'AUCUN contexte
/// d'entreprise : « réparer l'accès » (E5) = retirer de la nav les entrées qui, elles, l'exigent.
/// <para>
/// La VISIBILITÉ de l'entrée est gardée par <see cref="LiakontPermissions.Settings"/> (liakont.settings) :
/// la gestion des templates d'alerte relève du paramétrage du tenant, comme « Agents d'extraction » et la
/// page Paramétrage › Alertes (même rôle <c>parametrage</c>/<c>superviseur</c>). AUCUNE règle inventée :
/// la matrice §3 de <c>identity-permissions-liakont.md</c> reste intacte ; on réutilise une permission
/// EXISTANTE pour décider de la visibilité (même schéma que <see cref="JobNavVisibilityFilter"/>). SCOPED
/// car la visibilité dépend de l'utilisateur courant.
/// </para>
/// Le socle vendored n'est PAS modifié (CLAUDE.md n°11) : on délègue à
/// <see cref="NotificationNavSectionProvider.GetSection"/> pour ne pas dupliquer la définition socle
/// (titre, icône, ordre, libellé localisé de Templates) ; seuls la PROJECTION sur la seule entrée Templates
/// et le gardiennage par permission sont décidés ici. Une section vidée de ses items est omise par
/// <c>BuildNavTree</c> (qui filtre les sections à 0 item).
/// </remarks>
internal sealed class NotificationNavVisibilityFilter : INavSectionProvider
{
    /// <summary>Cible socle conservée : la page liste des templates email.</summary>
    private const string TemplatesHref = "/admin/notifications/templates";

    private readonly IStringLocalizer<NotificationResources> _localizer;
    private readonly IPermissionService _permissions;

    public NotificationNavVisibilityFilter(
        IStringLocalizer<NotificationResources> localizer,
        IPermissionService permissions)
    {
        _localizer = localizer;
        _permissions = permissions;
    }

    public NavSection GetSection()
    {
        var section = new NotificationNavSectionProvider(_localizer).GetSection();

        // Sans liakont.settings : section vidée → omise par BuildNavTree (lecture/operateur ne voient rien).
        if (!_permissions.HasPermission(LiakontPermissions.Settings))
        {
            return section with { Items = [] };
        }

        // On ne garde QUE Templates (par Href, stable et indépendant de la locale) : Règles de routage,
        // Webhooks, Simulation, SLA, Services, Integrations disparaissent de la nav Liakont.
        var templatesOnly = section.Items
            .Where(item => item.Href == TemplatesHref)
            .ToList();

        return section with { Items = templatesOnly };
    }
}
