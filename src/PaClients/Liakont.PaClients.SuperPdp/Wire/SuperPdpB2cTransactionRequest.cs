namespace Liakont.PaClients.SuperPdp.Wire;

using System.Collections.Generic;

/// <summary>
/// Corps de <c>POST /v1.beta/b2c_transactions</c> : <c>{ "data": [ b2c_transaction ] }</c> (OpenAPI
/// v1.24.0.beta). Les transactions sont stockées puis agrégées + envoyées au PPF par Super PDP selon le
/// <c>vat_regime</c> de la company (✅ sandbox 2026-06-22).
/// </summary>
internal sealed record SuperPdpB2cTransactionRequest
{
    /// <summary>Les transactions B2C à créer (<c>data</c>).</summary>
    public required IReadOnlyList<SuperPdpB2cTransaction> Data { get; init; }
}
