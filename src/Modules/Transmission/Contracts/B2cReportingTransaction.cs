namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Une transaction e-reporting B2C agrégée (flux 10.3), représentation <b>AGNOSTIQUE</b> de Liakont — le
/// plug-in PA la projette vers son format de fil (CLAUDE.md n°6/8 ; jamais un schéma PA concret ici).
/// Correspond au bloc DGFiP <c>Transactions</c> (TG-31) au grain <b>jour × devise</b> (catégorie et rôle
/// fixés par l'appelant — TMA1 / SE en enchères, F03 §2.5) ; les <see cref="Subtotals"/> portent le détail
/// par taux (TG-32). Numéros TT sourcés Annexe 6 (Format sémantique e-reporting). Les montants sont des bases
/// agrégées : pour la marge (TMA1), aucune TVA distincte n'existe au grain document (art. 297 E) — elle
/// n'apparaît qu'ici.
/// <para>Montants en <see cref="decimal"/> exclusivement, arrondi commercial half-up (CLAUDE.md n°1).</para>
/// </summary>
public sealed record B2cReportingTransaction
{
    /// <summary>Catégorie de transaction (TT-81, G1.68). Cas enchères : <see cref="EReportingTransactionCategory.Tma1"/>.</summary>
    public required EReportingTransactionCategory Category { get; init; }

    /// <summary>Rôle du déclarant (TT-15, G7.52). E-reporting des ventes : <see cref="EReportingDeclarantRole.Seller"/>.</summary>
    public required EReportingDeclarantRole Role { get; init; }

    /// <summary>Devise ISO 4217 de la transaction (ex. <c>EUR</c>).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Jour de la transaction — grain d'agrégation « jour » (F03 §2.5).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Montant total HT (TT-82) = somme des bases imposables des <see cref="Subtotals"/>. Decimal half-up (n°1).</summary>
    public required decimal TaxExclusiveAmount { get; init; }

    /// <summary>Montant total de TVA (TT-83) = somme des TVA des <see cref="Subtotals"/>. Decimal half-up (n°1).</summary>
    public required decimal TaxTotal { get; init; }

    /// <summary>Sous-totaux par taux (TT-86/87/88, G1.57), au moins un. Un sous-total par taux de TVA présent.</summary>
    public required IReadOnlyList<B2cReportingTransactionSubtotal> Subtotals { get; init; }
}
