namespace Liakont.Host.Documents;

using Liakont.Host.Components;
using Liakont.Modules.Documents.Contracts.DTOs;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la liste Documents (F10 §2.1 : N°, Date, Acheteur, Montant, Type, État).
/// Pilote <see cref="DeclaredListPage{TItem}"/> (filtres avancés, sélecteur de colonnes, export) : les
/// clés correspondent aux propriétés de <see cref="DocumentSummaryDto"/>. L'affichage (badge d'état,
/// libellé de type FR, formatage montant/date) est fourni par les ColumnTemplates de la page.
/// </summary>
internal sealed class DocumentColumnRegistry : ColumnRegistryBase<DocumentSummaryDto>
{
    protected override void Configure()
    {
        Column("DocumentNumber", "N°", "Document", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("IssueDate", "Date", "Document", ColumnDataType.Date, defaultVisible: true, sortOrder: 1);
        Column("CustomerName", "Acheteur", "Document", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("TotalGross", "Montant", "Document", ColumnDataType.Money, defaultVisible: true, sortOrder: 3);
        Column("DocumentType", "Type", "Document", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);

        // État : multi-select de filtre alimenté par les états canoniques (clés brutes) ; l'affichage
        // passe par le DocumentStateBadge (vocabulaire FR) via le ColumnTemplate de la page.
        Column(
            "State",
            "État",
            "Document",
            ColumnDataType.Enum,
            defaultVisible: true,
            sortOrder: 5,
            allowedValues: DocumentStateDisplay.CanonicalOrder);

        // Dernière mise à jour : masquée par défaut (axe de tri/filtre disponible, hors encombrement visuel).
        Column("LastUpdateUtc", "Mis à jour le", "Document", ColumnDataType.Date, defaultVisible: false, sortOrder: 6);
    }
}
