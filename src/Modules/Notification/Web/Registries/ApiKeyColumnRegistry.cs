namespace Stratum.Modules.Notification.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Notification.Contracts.DTOs;

internal sealed class ApiKeyColumnRegistry : ColumnRegistryBase<ApiKeyDto>
{
    protected override void Configure()
    {
        Column("Name", "Nom", "ApiKey", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("KeyPrefix", "Clé (préfixe)", "ApiKey", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Scopes", "Scopes", "ApiKey", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("RateLimit", "Limite /h", "ApiKey", ColumnDataType.Number, defaultVisible: true, sortOrder: 3);
        Column("IsRevoked", "Statut", "ApiKey", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 4);
        Column("CreatedAt", "Créée le", "ApiKey", ColumnDataType.Date, defaultVisible: true, sortOrder: 5);
        Column("ExpiresAt", "Expire le", "ApiKey", ColumnDataType.Date, defaultVisible: true, sortOrder: 6);
    }
}
