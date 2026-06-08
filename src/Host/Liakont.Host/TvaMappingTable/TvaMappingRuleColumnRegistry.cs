namespace Liakont.Host.TvaMappingTable;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la table de mapping TVA (F03 §4.1 : régime source, libellé, part, catégorie,
/// taux, VATEX). Pilote <see cref="DeclaredListPage{TItem}"/> (filtres avancés, sélecteur de colonnes,
/// export) : les clés correspondent aux propriétés de <see cref="MappingRuleDto"/>. L'affichage
/// (badge catégorie, taux mode-aware, VATEX/libellé optionnels) est fourni par les ColumnTemplates de
/// la page.
/// </summary>
internal sealed class TvaMappingRuleColumnRegistry : ColumnRegistryBase<MappingRuleDto>
{
    protected override void Configure()
    {
        Column("SourceRegimeCode", "Régime source", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Label", "Libellé", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Part", "Part", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);

        // Catégorie : code UNCL5305 (S, AA, AAA, Z, E, AE, G, K, O). Texte (PAS Enum) : le DTO l'expose
        // déjà par son code string ; l'affichage passe par un CategoryBadge via le ColumnTemplate.
        Column("Category", "Catégorie", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);

        // Taux : tri/filtre sur la valeur (decimal) ; l'affichage (mode « Calculé depuis la source » vs
        // pourcentage fixe) est rendu par le ColumnTemplate de la page.
        Column("RateValue", "Taux", "RègleTVA", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);

        Column("Vatex", "VATEX", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
    }
}
