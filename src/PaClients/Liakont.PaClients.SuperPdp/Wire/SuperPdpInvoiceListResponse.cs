namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Forme ENVELOPPÉE d'une liste de factures (<c>{ "invoices": [...] }</c>) — DTO propriétaire,
/// <c>internal</c>. Utilisée par la relecture d'idempotence (F14 §4.1 : relire la liste du compte pour
/// raccrocher une facture déjà créée). La forme « fil » exacte reste à confirmer en sandbox (PAS03, O2) :
/// le parseur tolère AUSSI un tableau JSON nu, et toute forme non reconnue est traitée comme NON
/// CONCLUANTE — jamais comme « facture absente » — pour ne jamais risquer un doublon (CLAUDE.md n°3).
/// </summary>
internal sealed record SuperPdpInvoiceListResponse
{
    /// <summary>Les factures du compte renvoyées par la liste, ou <c>null</c> si la clé est absente.</summary>
    public IReadOnlyList<SuperPdpInvoiceResponse>? Invoices { get; init; }
}
