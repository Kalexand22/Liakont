namespace Stratum.Modules.Identity.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Identity.Contracts.DTOs;

internal sealed class TeamColumnRegistry : ColumnRegistryBase<TeamDto>
{
    protected override void Configure()
    {
        Column("Code", "Code", "Équipe", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Name", "Nom", "Équipe", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Description", "Description", "Équipe", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("ServiceCode", "Service", "Équipe", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column("MemberCount", "Membres", "Équipe", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);
        Column("IsActive", "Actif", "Équipe", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 5);
        Column("CreatedAt", "Créé le", "Équipe", ColumnDataType.Date, defaultVisible: false, sortOrder: 6);
    }
}
