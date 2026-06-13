namespace Liakont.Host.Clients;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Colonnes de l'écran « Clients » (OPS03), pilotant <c>DeclaredListPage</c> (tri, recherche, export) —
/// aucune grille « maison ». Le badge de statut et le formatage de date sont portés par les
/// ColumnTemplates de la page.
/// </summary>
internal sealed class ClientColumnRegistry : ColumnRegistryBase<ClientConsoleLine>
{
    protected override void Configure()
    {
        Column("DisplayName", "Nom", "Client", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Siren", "SIREN", "Client", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Statut", "Statut", "Client", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("AgentCount", "Agents", "Client", ColumnDataType.Number, defaultVisible: true, sortOrder: 3);
        Column("ProvisionedAt", "Créé le", "Client", ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
        Column("TenantId", "Identifiant", "Client", ColumnDataType.Text, defaultVisible: false, sortOrder: 5);
    }
}
