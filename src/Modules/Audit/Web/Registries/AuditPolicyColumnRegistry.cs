namespace Stratum.Modules.Audit.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Audit.Contracts.DTOs;

internal sealed class AuditPolicyColumnRegistry : ColumnRegistryBase<AuditPolicyDto>
{
    protected override void Configure()
    {
        Column("EntityType", "Type d'entité", "AuditPolicy", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("ModuleSource", "Module", "AuditPolicy", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("IsEnabled", "Actif", "AuditPolicy", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 2);
        Column("TrackedFields", "Champs suivis", "AuditPolicy", ColumnDataType.Number, defaultVisible: true, sortOrder: 3);
        Column("CreatedAt", "Créé le", "AuditPolicy", ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
        Column("UpdatedAt", "Modifié le", "AuditPolicy", ColumnDataType.Date, defaultVisible: false, sortOrder: 5);
    }
}
