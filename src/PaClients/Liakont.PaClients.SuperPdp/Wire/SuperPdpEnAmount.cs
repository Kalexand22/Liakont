namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>Montant accompagné de sa devise (forme du <c>total_vat_amount</c> de l'OpenAPI).</summary>
internal sealed record SuperPdpEnAmount
{
    /// <summary>Le montant, en <see cref="decimal"/> (CLAUDE.md n°1).</summary>
    public required decimal Value { get; init; }

    /// <summary>Devise ISO 4217 du montant.</summary>
    public required string CurrencyCode { get; init; }
}
