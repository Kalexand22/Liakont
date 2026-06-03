namespace Stratum.Modules.Notification.Web;

using Microsoft.Extensions.Localization;
using Stratum.Common.UI.Models;

public sealed class NotificationNavSectionProvider(IStringLocalizer<NotificationResources> localizer) : INavSectionProvider
{
    public NavSection GetSection() => new(
        Title: localizer["Nav_Notifications"],
        Icon: "bi-bell",
        Order: 50,
        Items:
        [
            new NavItem(localizer["Nav_Templates"], "/admin/notifications/templates"),
            new NavItem(localizer["Nav_RoutingRules"], "/admin/notifications/routing"),
            new NavItem(localizer["Nav_Webhooks"], "/admin/notifications/webhooks"),
            new NavItem(localizer["Nav_Simulation"], "/admin/notifications/preview"),
            new NavItem(localizer["Nav_Sla"], "/admin/sla"),
            new NavItem(localizer["Nav_Services"], "/admin/catalog/services"),
            new NavItem(localizer["Nav_Integrations"], "/admin/integrations"),
        ]);
}
