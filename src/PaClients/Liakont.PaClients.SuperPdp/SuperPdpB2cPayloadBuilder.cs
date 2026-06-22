namespace Liakont.PaClients.SuperPdp;

using System;
using System.Globalization;
using System.Linq;
using Liakont.Agent.Contracts;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Construit le corps <c>b2c_transactions</c> de Super PDP à partir d'une <see cref="B2cReportingTransaction"/>
/// AGNOSTIQUE (frontière n°6 : le mapping vers le format de fil PA vit ICI, jamais hors du plug-in). Codes
/// TT-81 / TT-15 canoniques (<see cref="EReportingCodes"/>), montants formatés en chaîne <b>invariante</b>
/// (l'API attend <c>string (decimal)</c>), <c>id</c> readOnly JAMAIS émis. Aucune règle fiscale ici : les
/// montants viennent déjà agrégés/arrondis de la plateforme (CLAUDE.md n°1/2).
/// </summary>
internal static class SuperPdpB2cPayloadBuilder
{
    /// <summary>Projette une transaction agrégée Liakont vers le corps <c>{ data: [ b2c_transaction ] }</c>.</summary>
    /// <param name="transaction">La transaction e-reporting B2C agnostique.</param>
    /// <returns>Le corps de requête Super PDP.</returns>
    public static SuperPdpB2cTransactionRequest Build(B2cReportingTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Garde fail-closed à la frontière n°6 : jamais POSTer une déclaration fiscale incohérente (CLAUDE.md n°3).
        // Le producteur (B2cTransactionAggregationCalculator) garantit déjà la cohérence ; ceci borde tout autre caller.
        if (transaction.Subtotals.Count == 0)
        {
            throw new ArgumentException(
                "Transaction e-reporting B2C sans aucun sous-total de taux (agrégat incohérent).",
                nameof(transaction));
        }

        var sumTaxable = transaction.Subtotals.Sum(s => s.TaxableAmount);
        var sumTax = transaction.Subtotals.Sum(s => s.TaxTotal);
        if (sumTaxable != transaction.TaxExclusiveAmount || sumTax != transaction.TaxTotal)
        {
            throw new ArgumentException(
                $"Agrégat e-reporting B2C incohérent : Σ sous-totaux (HT {sumTaxable} / TVA {sumTax}) ≠ totaux (HT {transaction.TaxExclusiveAmount} / TVA {transaction.TaxTotal}).",
                nameof(transaction));
        }

        return new SuperPdpB2cTransactionRequest
        {
            Data =
            [
                new SuperPdpB2cTransaction
                {
                    CategoryCode = transaction.Category.ToTransactionCategoryCode(),
                    Currency = transaction.CurrencyCode,
                    Date = transaction.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    RoleCode = transaction.Role.ToDeclarantRoleCode(),
                    TaxExclusiveAmount = Amount(transaction.TaxExclusiveAmount),
                    TaxTotal = Amount(transaction.TaxTotal),
                    TaxSubtotals = transaction.Subtotals
                        .Select(s => new SuperPdpB2cSubtotal
                        {
                            TaxPercent = s.TaxPercent.ToString("0.0#", CultureInfo.InvariantCulture),
                            TaxableAmount = Amount(s.TaxableAmount),
                            TaxTotal = Amount(s.TaxTotal),
                        })
                        .ToList(),
                },
            ],
        };
    }

    // Arrondi commercial half-up explicite (PivotRounding, CLAUDE.md n°1) AVANT formatage : ne jamais laisser
    // ToString("0.00") appliquer l'arrondi banquier (ToEven) sur une valeur non pré-arrondie à une frontière publique.
    private static string Amount(decimal value) =>
        PivotRounding.RoundAmount(value).ToString("0.00", CultureInfo.InvariantCulture);
}
