namespace Liakont.Host.TvaDeclaration;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la page « TVA / Déclaration » (L2) : agrégats du mois par devise × taux (base HT +
/// TVA sur marge à reporter). Alimente le gabarit <c>DeclaredListPage</c> (recherche, filtres, colonnes
/// configurables, export, tri) — aucune grille « maison » (directive opérateur). Les clés sont les propriétés de
/// <see cref="TvaDeclarationRow"/> ; le formatage français (taux, montants) est porté par les
/// <c>ColumnTemplates</c> de la page.
/// </summary>
internal sealed class TvaDeclarationColumnRegistry : ColumnRegistryBase<TvaDeclarationRow>
{
    protected override void Configure()
    {
        Column(nameof(TvaDeclarationRow.RatePercent), "Taux", "TvaDeclaration", ColumnDataType.Number, defaultVisible: true, sortOrder: 0);
        Column(nameof(TvaDeclarationRow.Currency), "Devise", "TvaDeclaration", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column(nameof(TvaDeclarationRow.MarginBaseHt), "Base HT (marge)", "TvaDeclaration", ColumnDataType.Number, defaultVisible: true, sortOrder: 2);
        Column(nameof(TvaDeclarationRow.MarginVat), "TVA sur marge", "TvaDeclaration", ColumnDataType.Number, defaultVisible: true, sortOrder: 3);
        Column(nameof(TvaDeclarationRow.DocumentCount), "Pièces", "TvaDeclaration", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);
    }
}
