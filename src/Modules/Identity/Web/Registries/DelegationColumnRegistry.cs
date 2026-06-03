namespace Stratum.Modules.Identity.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Identity.Contracts.DTOs;

internal sealed class DelegationColumnRegistry : ColumnRegistryBase<DelegationDto>
{
    protected override void Configure()
    {
        Column("DelegatorName", "Délégant", "Délégation", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("DelegateName", "Délégataire", "Délégation", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Scope", "Portée", "Délégation", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("ValidFrom", "Début", "Délégation", ColumnDataType.Date, defaultVisible: true, sortOrder: 3);
        Column("ValidUntil", "Fin", "Délégation", ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
        Column("Reason", "Motif", "Délégation", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
        Column("IsActive", "Active", "Délégation", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 6);
        Column("CreatedAt", "Créé le", "Délégation", ColumnDataType.Date, defaultVisible: false, sortOrder: 7);
    }
}
