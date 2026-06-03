namespace Stratum.Modules.Notification.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Notification.Contracts.DTOs;

internal sealed class WebhookSubscriptionColumnRegistry : ColumnRegistryBase<WebhookSubscriptionDto>
{
    protected override void Configure()
    {
        Column("Name", "Nom", "Webhook", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("EventType", "Type événement", "Webhook", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("TargetUrl", "URL cible", "Webhook", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("IsActive", "Actif", "Webhook", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 3);
        Column("CreatedAt", "Créé le", "Webhook", ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
        Column("UpdatedAt", "Modifié le", "Webhook", ColumnDataType.Date, defaultVisible: false, sortOrder: 5);
    }
}
