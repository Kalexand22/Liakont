namespace Liakont.Host.Documents;

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

        // Famille de pièce (BUG-20) : bordereau acheteur/vendeur, facture client, note d'honoraires — dérivée
        // de la référence source par le ColumnTemplate de la page (DocumentFamilyDisplay). La clé porte la
        // référence source brute (un BA et un BV de même numéro ont des références distinctes → tri/recherche
        // fiables) ; l'affichage écran montre le libellé FR de la famille, distinction absente du « Type ».
        Column("SourceReference", "Famille de pièce", "Document", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);

        // État : colonne Texte (PAS Enum). Un Enum exposerait les CLÉS BRUTES anglaises (Issued,
        // RejectedByPa…) dans le filtre avancé de la grille — incompatible avec « 100 % français »
        // (CLAUDE.md n°12). L'affichage passe par DocumentStateBadge (vocabulaire FR) via le
        // ColumnTemplate de la page, et le filtrage d'état se fait par le sélecteur français de la page
        // (+ pastilles de synthèse cliquables), pas par le filtre brut de colonne.
        Column("State", "État", "Document", ColumnDataType.Text, defaultVisible: true, sortOrder: 6);

        // Dernière mise à jour : masquée par défaut (axe de tri/filtre disponible, hors encombrement visuel).
        Column("LastUpdateUtc", "Mis à jour le", "Document", ColumnDataType.Date, defaultVisible: false, sortOrder: 7);
    }
}
