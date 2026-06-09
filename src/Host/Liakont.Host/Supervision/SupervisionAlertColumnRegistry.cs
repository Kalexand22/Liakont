namespace Liakont.Host.Supervision;

using Liakont.Modules.Supervision.Contracts.DTOs;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la liste des alertes d'un tenant (SUP02, détail) : Gravité, Alerte (message
/// opérateur FR), Déclenchée le, Résolue le, Acquittée par. Pilote <see cref="DeclaredListPage{TItem}"/> ;
/// les clés correspondent aux propriétés d'<see cref="AlertDto"/>. L'affichage (badge de gravité FR,
/// formatage des dates, libellé « Active ») est fourni par les ColumnTemplates de la page.
/// </summary>
internal sealed class SupervisionAlertColumnRegistry : ColumnRegistryBase<AlertDto>
{
    protected override void Configure()
    {
        // Gravité : Texte (pas Enum) — un Enum exposerait les clés brutes anglaises dans le filtre de la
        // grille ; l'affichage FR passe par le ColumnTemplate (CLAUDE.md n°12).
        Column("Severity", "Gravité", "Alerte", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Detail", "Alerte", "Alerte", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("TriggeredUtc", "Déclenchée le", "Alerte", ColumnDataType.Date, defaultVisible: true, sortOrder: 2);
        Column("ResolvedUtc", "Résolue le", "Alerte", ColumnDataType.Date, defaultVisible: true, sortOrder: 3);
        Column("AcknowledgedBy", "Acquittée par", "Alerte", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);
    }
}
