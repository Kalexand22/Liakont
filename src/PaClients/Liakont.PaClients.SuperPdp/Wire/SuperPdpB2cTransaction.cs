namespace Liakont.PaClients.SuperPdp.Wire;

using System.Collections.Generic;

/// <summary>
/// Une transaction e-reporting B2C (schéma <c>b2c_transaction</c> de l'OpenAPI v1.24.0.beta, ✅ POST sandbox
/// 2026-06-22, id serveur 585). Le champ <c>id</c> est <b>readOnly</b> (assigné par le serveur) → JAMAIS
/// émis (omis). Montants en chaîne decimal (l'API attend <c>string (decimal)</c>).
/// </summary>
internal sealed record SuperPdpB2cTransaction
{
    /// <summary>Catégorie de transaction TT-81 (<c>category_code</c>), ex. <c>TMA1</c> (G1.68).</summary>
    public required string CategoryCode { get; init; }

    /// <summary>Devise ISO 4217 (<c>currency</c>), ex. <c>EUR</c>.</summary>
    public required string Currency { get; init; }

    /// <summary>Jour de la transaction au format ISO 8601 (<c>date</c>), ex. <c>2026-06-22</c>.</summary>
    public required string Date { get; init; }

    /// <summary>Rôle du déclarant TT-15 (<c>role_code</c>) : <c>SE</c> (vente) ou <c>BY</c> (achat) — G7.52.</summary>
    public required string RoleCode { get; init; }

    /// <summary>Montant total HT (<c>tax_exclusive_amount</c>).</summary>
    public required string TaxExclusiveAmount { get; init; }

    /// <summary>Montant total de TVA (<c>tax_total</c>).</summary>
    public required string TaxTotal { get; init; }

    /// <summary>Sous-totaux par taux (<c>tax_subtotals</c>).</summary>
    public required IReadOnlyList<SuperPdpB2cSubtotal> TaxSubtotals { get; init; }
}
