namespace Stratum.Modules.Identity.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Identity.Contracts.DTOs;

internal sealed class UserColumnRegistry : ColumnRegistryBase<UserDto>
{
    protected override void Configure()
    {
        Column("DisplayName", "Nom", "User", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Username", "Identifiant", "User", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Email", "E-mail", "User", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("IsActive", "Statut", "User", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 3);
        Column("Roles", "Rôles", "User", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);
        Column("LastLoginAt", "Dernière connexion", "User", ColumnDataType.Date, defaultVisible: true, sortOrder: 5);
        Column("ExternalId", "ID externe", "User", ColumnDataType.Text, defaultVisible: false, sortOrder: 6);
    }
}
