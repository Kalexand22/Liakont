namespace Liakont.Host.Payments;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes de la page Encaissements (WEB06, F10 §2.4) : agrégats jour×taux de l'e-reporting de
/// paiement. Alimente le gabarit <c>DeclaredListPage</c> (recherche « / », filtres avancés, colonnes
/// configurables, export, tri) — aucune grille « maison » (directive opérateur 2026-06-07). Les clés sont
/// les propriétés de <see cref="PaymentAggregateRow"/> (lecture/tri par la grille) ; le formatage français
/// (taux « 20 % », montants <c>N2</c>, badge d'état) est porté par les <c>ColumnTemplates</c> de la page.
/// </summary>
internal sealed class PaymentAggregateColumnRegistry : ColumnRegistryBase<PaymentAggregateRow>
{
    protected override void Configure()
    {
        Column(nameof(PaymentAggregateRow.AggregateDate), "Jour", "Payment", ColumnDataType.Date, defaultVisible: true, sortOrder: 0);
        Column(nameof(PaymentAggregateRow.VatRate), "Taux", "Payment", ColumnDataType.Number, defaultVisible: true, sortOrder: 1);
        Column(nameof(PaymentAggregateRow.TaxableBase), "Base HT", "Payment", ColumnDataType.Money, defaultVisible: true, sortOrder: 2);
        Column(nameof(PaymentAggregateRow.VatAmount), "TVA", "Payment", ColumnDataType.Money, defaultVisible: true, sortOrder: 3);
        Column(nameof(PaymentAggregateRow.Status), "État", "Payment", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);
        Column(nameof(PaymentAggregateRow.Reason), "Motif", "Payment", ColumnDataType.Text, defaultVisible: true, sortOrder: 5);
    }
}
