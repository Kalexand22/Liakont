namespace Liakont.Host.B2cReporting;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la page des émissions e-reporting B2C de la marge (B4) : agrégats transmis
/// jour×devise×catégorie×rôle + état d'émission. Alimente le gabarit <c>DeclaredListPage</c> (recherche,
/// filtres, colonnes configurables, export, tri) — aucune grille « maison » (directive opérateur). Les clés
/// sont les propriétés de <see cref="B2cMarginEmissionRow"/> ; le formatage français (date, badge d'état)
/// est porté par les <c>ColumnTemplates</c> de la page.
/// </summary>
internal sealed class B2cMarginEmissionColumnRegistry : ColumnRegistryBase<B2cMarginEmissionRow>
{
    protected override void Configure()
    {
        Column(nameof(B2cMarginEmissionRow.AggregateDate), "Jour", "B2cMargin", ColumnDataType.Date, defaultVisible: true, sortOrder: 0);
        Column(nameof(B2cMarginEmissionRow.Currency), "Devise", "B2cMargin", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column(nameof(B2cMarginEmissionRow.Category), "Catégorie", "B2cMargin", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column(nameof(B2cMarginEmissionRow.Role), "Rôle", "B2cMargin", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column(nameof(B2cMarginEmissionRow.DocumentCount), "Pièces", "B2cMargin", ColumnDataType.Number, defaultVisible: true, sortOrder: 4);
        Column(nameof(B2cMarginEmissionRow.Status), "État", "B2cMargin", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
        Column(nameof(B2cMarginEmissionRow.PaEmissionId), "Id plateforme", "B2cMargin", ColumnDataType.Text, defaultVisible: true, sortOrder: 6);
        Column(nameof(B2cMarginEmissionRow.Detail), "Détail", "B2cMargin", ColumnDataType.Text, defaultVisible: true, sortOrder: 7);
        Column(nameof(B2cMarginEmissionRow.LastActivityUtc), "Dernière activité", "B2cMargin", ColumnDataType.Date, defaultVisible: false, sortOrder: 8);
    }
}
