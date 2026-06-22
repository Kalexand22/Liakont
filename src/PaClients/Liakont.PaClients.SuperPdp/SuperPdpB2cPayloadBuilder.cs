namespace Liakont.PaClients.SuperPdp;

using System;
using System.Globalization;
using System.Linq;
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

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
