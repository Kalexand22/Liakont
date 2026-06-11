namespace Liakont.Host.AgentManagement;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Colonnes de la liste du parc d'agents (WEB09), pilotant <c>DeclaredListPage</c> (tri, recherche, choix /
/// réordonnancement des colonnes, export) — aucune grille « maison ». Le rendu coloré de l'état et le
/// formatage du heartbeat sont portés par les ColumnTemplates de la page ; le tri / la recherche / l'export
/// reposent sur les propriétés de <see cref="AgentConsoleLine"/> nommées ici.
/// </summary>
internal sealed class AgentConsoleColumnRegistry : ColumnRegistryBase<AgentConsoleLine>
{
    protected override void Configure()
    {
        Column("Name", "Nom", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("StateLabel", "État", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("LastSeenUtc", "Dernier contact", "Agent", ColumnDataType.Date, defaultVisible: true, sortOrder: 2);
        Column("Version", "Version", "Agent", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column("KeyPrefix", "Préfixe de clé", "Agent", ColumnDataType.Text, defaultVisible: false, sortOrder: 4);
        Column("CreatedAt", "Créé le", "Agent", ColumnDataType.Date, defaultVisible: false, sortOrder: 5);
    }
}
