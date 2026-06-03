namespace Stratum.Modules.Notification.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Notification.Contracts.DTOs;

internal sealed class ServiceDefinitionColumnRegistry : ColumnRegistryBase<ServiceDefinitionDto>
{
    protected override void Configure()
    {
        Column("Code", "Code", "Service", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Name", "Nom", "Service", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Email", "E-mail", "Service", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("ManagerName", "Responsable", "Service", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column("DefaultSlaHours", "SLA (h)", "Service", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);
        Column("Competences", "Compétences", "Service", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
        Column("Color", "Couleur", "Service", ColumnDataType.Text, defaultVisible: false, sortOrder: 6);
        Column("IsActive", "Actif", "Service", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 7);
        Column("CreatedAt", "Créé le", "Service", ColumnDataType.Date, defaultVisible: false, sortOrder: 8);
    }
}
