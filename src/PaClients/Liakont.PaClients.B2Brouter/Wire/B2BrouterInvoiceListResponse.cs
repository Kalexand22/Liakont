namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Forme ENVELOPPÉE d'une liste de factures (<c>{ "invoices": [...] }</c>) — DTO propriétaire,
/// <c>internal</c>. Utilisée par la relecture d'idempotence (F05 §4.2 : « relire via GET la liste des
/// factures du compte »). La forme « fil » exacte de la liste reste à confirmer en staging (suite
/// PAB04) : le parseur tolère donc AUSSI un tableau JSON nu (voir <see cref="B2BrouterResponseMapper"/>),
/// et toute forme non reconnue est traitée comme NON CONCLUANTE — jamais comme « facture absente » —
/// pour ne jamais risquer un doublon (CLAUDE.md n°3).
/// </summary>
internal sealed record B2BrouterInvoiceListResponse
{
    /// <summary>Les factures du compte renvoyées par la liste, ou <c>null</c> si la clé est absente.</summary>
    public IReadOnlyList<B2BrouterInvoiceResponse>? Invoices { get; init; }
}
