namespace Stratum.Modules.Audit.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Audit.Contracts.DTOs;

internal sealed class AuditEntryColumnRegistry : ColumnRegistryBase<AuditSearchResultDto>
{
    private static readonly string[] _activityTypes =
        ["created", "updated", "deleted", "status_changed", "exported", "imported"];

    protected override void Configure()
    {
        Column("CreatedAt", "Date", "AuditEntry", ColumnDataType.Date, defaultVisible: true, sortOrder: 0);
        Column("ActorId", "Utilisateur", "AuditEntry", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("EntityType", "Entité", "AuditEntry", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("EntityId", "ID entité", "AuditEntry", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column(
            "ActivityType",
            "Action",
            "AuditEntry",
            ColumnDataType.Enum,
            defaultVisible: true,
            sortOrder: 4,
            allowedValues: _activityTypes);
        Column("Description", "Description", "AuditEntry", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
        Column("ChangeCount", "Champs", "AuditEntry", ColumnDataType.Number, defaultVisible: true, sortOrder: 6);
    }
}
