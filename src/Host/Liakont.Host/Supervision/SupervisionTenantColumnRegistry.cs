namespace Liakont.Host.Supervision;

using Liakont.Modules.Supervision.Contracts.DTOs;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la vue d'ensemble multi-tenants de la supervision (SUP02, F12 §5) : Tenant,
/// Alertes, Agents, Dernier heartbeat, et compteurs de documents (Bloqués / Rejetés PA / En attente).
/// Pilote <see cref="DeclaredListPage{TItem}"/> ; les clés correspondent aux propriétés de
/// <see cref="TenantSupervisionRowDto"/>. L'affichage (badge de gravité, marqueur d'échec de lecture,
/// formatage des dates) est fourni par les ColumnTemplates de la page.
/// </summary>
internal sealed class SupervisionTenantColumnRegistry : ColumnRegistryBase<TenantSupervisionRowDto>
{
    protected override void Configure()
    {
        Column("DisplayName", "Tenant", "Supervision", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("ActiveAlertCount", "Alertes", "Supervision", ColumnDataType.Number, defaultVisible: true, sortOrder: 1);
        Column("AgentCount", "Agents", "Supervision", ColumnDataType.Number, defaultVisible: true, sortOrder: 2);
        Column("LastAgentSeenUtc", "Dernier heartbeat", "Supervision", ColumnDataType.Date, defaultVisible: true, sortOrder: 3);
        Column("BlockedDocumentCount", "Bloqués", "Supervision", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);
        Column("RejectedByPaDocumentCount", "Rejetés PA", "Supervision", ColumnDataType.Number, defaultVisible: true, sortOrder: 5);
        Column("PendingDocumentCount", "En attente", "Supervision", ColumnDataType.Number, defaultVisible: true, sortOrder: 6);
    }
}
