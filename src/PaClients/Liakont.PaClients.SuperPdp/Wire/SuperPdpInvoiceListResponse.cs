namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// La liste paginée de factures de <c>GET /v1.beta/invoices</c> (✅ forme confirmée sandbox 2026-06-12 :
/// <c>{"data":[…],"count":n,"has_before":…,"has_after":…}</c>). Utilisée par la relecture d'idempotence
/// (F14 §4.1 : raccrocher par <c>external_id</c> une facture déjà créée). Toute forme non reconnue est
/// traitée comme NON CONCLUANTE — jamais comme « facture absente » — pour ne jamais risquer un doublon
/// (CLAUDE.md n°3).
/// </summary>
internal sealed record SuperPdpInvoiceListResponse
{
    /// <summary>
    /// Les factures de la page, ou <c>null</c> si la clé est absente (forme non concluante). La page est
    /// PARTIELLE par construction (pagination par curseur, <c>has_after</c>) : « external_id absent » est
    /// donc toujours traité comme NON CONCLUANT par l'appelant — jamais « facture absente » (CLAUDE.md n°3).
    /// </summary>
    public IReadOnlyList<SuperPdpInvoiceResponse>? Data { get; init; }
}
