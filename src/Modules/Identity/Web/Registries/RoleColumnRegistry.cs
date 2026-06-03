namespace Stratum.Modules.Identity.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Identity.Contracts.DTOs;

internal sealed class RoleColumnRegistry : ColumnRegistryBase<RoleDto>
{
    protected override void Configure()
    {
        Column("Name", "Nom", "Role", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Description", "Description", "Role", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("IsSystem", "Système", "Role", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 2);
    }
}
