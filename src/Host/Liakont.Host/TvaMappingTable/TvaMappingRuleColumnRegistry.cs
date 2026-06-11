namespace Liakont.Host.TvaMappingTable;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la table de mapping TVA (F03 §4.1 : régime source, libellé, composante,
/// catégorie, taux, VATEX). Pilote <see cref="DeclaredListPage{TItem}"/> (filtres avancés, sélecteur de
/// colonnes, export) : les clés correspondent aux propriétés de <see cref="MappingRuleDto"/>. L'affichage
/// (badge catégorie, taux mode-aware, composante/VATEX/libellé) est fourni par les ColumnTemplates de
/// la page. La colonne « Composante » (clé technique <c>Part</c>) n'est registrée QUE si le vertical
/// enchères est actif (décision E2 / lot FIX2 : hors vertical, la notion n'apparaît nulle part).
/// </summary>
internal sealed class TvaMappingRuleColumnRegistry : ColumnRegistryBase<MappingRuleDto>
{
    private readonly bool _includeComposante;

    /// <param name="includeComposante">
    /// <c>true</c> quand le vertical « vente aux enchères » est actif : la colonne « Composante » est
    /// exposée. <c>false</c> (défaut produit) : la notion est entièrement masquée (E2).
    /// </param>
    public TvaMappingRuleColumnRegistry(bool includeComposante)
    {
        // Configure() est appelé en initialisation PARESSEUSE (au premier accès aux colonnes), jamais
        // dans le constructeur de base : ce champ est donc déjà positionné quand Configure() s'exécute.
        _includeComposante = includeComposante;
    }

    protected override void Configure()
    {
        Column("SourceRegimeCode", "Régime source", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Label", "Libellé", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);

        if (_includeComposante)
        {
            // Clé technique « Part » conservée (tri / filtre / export sur la propriété brute du DTO, qui
            // restitue le code technique — même comportement que la colonne Catégorie) ; libellé d'affichage
            // « Composante » et valeur rendue (Autre → « Hors Enchères ») via le ColumnTemplate à l'écran.
            Column("Part", "Composante", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        }

        // Catégorie : code UNCL5305 (S, AA, AAA, Z, E, AE, G, K, O). Texte (PAS Enum) : le DTO l'expose
        // déjà par son code string ; l'affichage passe par un CategoryBadge via le ColumnTemplate.
        Column("Category", "Catégorie", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);

        // Taux : tri/filtre sur la valeur (decimal) ; l'affichage (mode « Calculé depuis la source » vs
        // pourcentage fixe) est rendu par le ColumnTemplate de la page.
        Column("RateValue", "Taux", "RègleTVA", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);

        Column("Vatex", "VATEX", "RègleTVA", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
    }
}
