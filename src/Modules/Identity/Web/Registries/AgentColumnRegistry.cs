namespace Stratum.Modules.Identity.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Identity.Contracts.DTOs;

internal sealed class AgentColumnRegistry : ColumnRegistryBase<AgentDto>
{
    protected override void Configure()
    {
        Column("DisplayName", "Nom", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Username", "Identifiant", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Email", "E-mail", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("ServiceCode", "Service", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column("Title", "Fonction", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);
        Column("Phone", "Téléphone", "Agent", ColumnDataType.Text, defaultVisible: false, sortOrder: 5);
        Column("OfficeLocation", "Bureau", "Agent", ColumnDataType.Text, defaultVisible: false, sortOrder: 6);
        Column("Teams", "Équipes", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 7);
        Column("CompetenceCount", "Compétences", "Agent", ColumnDataType.Number, defaultVisible: true, sortOrder: 8);
        Column("IsActive", "Actif", "Agent", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 9);
        Column("CreatedAt", "Créé le", "Agent", ColumnDataType.Date, defaultVisible: false, sortOrder: 10);
    }
}
