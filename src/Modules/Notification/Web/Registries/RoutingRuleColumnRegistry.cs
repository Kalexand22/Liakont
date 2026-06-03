namespace Stratum.Modules.Notification.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Notification.Contracts.DTOs;

internal sealed class RoutingRuleColumnRegistry : ColumnRegistryBase<RoutingRuleDto>
{
    private static readonly string[] _recipientTypes = ["ServiceEmail", "Role", "User"];

    protected override void Configure()
    {
        Column("Code", "Code", "RoutingRule", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Name", "Nom", "RoutingRule", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("EntityType", "Type entité", "RoutingRule", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);

        // ServiceCode is a soft enum populated from IServiceDefinitionQueries (dynamic
        // per company), so it stays Text until the registry supports a value provider.
        Column("ServiceCode", "Service", "RoutingRule", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column(
            "RecipientType",
            "Type destinataire",
            "RoutingRule",
            ColumnDataType.Enum,
            defaultVisible: true,
            sortOrder: 4,
            allowedValues: _recipientTypes);
        Column("RecipientValue", "Destinataire", "RoutingRule", ColumnDataType.Text, defaultVisible: false, sortOrder: 5);
        Column("Priority", "Priorité", "RoutingRule", ColumnDataType.Number, defaultVisible: true, sortOrder: 6);
        Column("IsActive", "Actif", "RoutingRule", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 7);
        Column("CreatedAt", "Créé le", "RoutingRule", ColumnDataType.Date, defaultVisible: false, sortOrder: 8);
    }
}
