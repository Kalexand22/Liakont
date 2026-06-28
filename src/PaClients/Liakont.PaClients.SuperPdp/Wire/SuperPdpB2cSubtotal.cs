namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Sous-total de TVA par taux d'un <see cref="SuperPdpB2cTransaction"/> (schéma <c>b2c_transaction_subtotal</c>
/// de l'OpenAPI v1.24.0.beta). Montants en <b>chaîne</b> decimal : l'API attend <c>string (decimal)</c>
/// (✅ confirmé POST sandbox 2026-06-22).
/// </summary>
internal sealed record SuperPdpB2cSubtotal
{
    /// <summary>Taux de TVA en pourcentage (<c>tax_percent</c>).</summary>
    public required string TaxPercent { get; init; }

    /// <summary>Base imposable du taux (<c>taxable_amount</c>).</summary>
    public required string TaxableAmount { get; init; }

    /// <summary>Montant de TVA du taux (<c>tax_total</c>).</summary>
    public required string TaxTotal { get; init; }
}
