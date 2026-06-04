namespace Liakont.Modules.Validation.Contracts.CreditNotes;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Recherche, pour un tenant donné, l'état d'une facture d'origine référencée par un avoir
/// (F07-F08 §B.5). Permet à la règle des avoirs (VAL04) de distinguer un avoir régulier d'un avoir
/// ORPHELIN sans jamais fabriquer de référence (CLAUDE.md n°2). L'implémentation réelle, branchée sur
/// le module Documents (suivi des documents émis), arrive avec TRK03 ; jusque-là un double de test
/// suffit (même schéma que l'<c>IIssuedDocumentLookup</c> d'unicité de VAL03).
/// </summary>
/// <remarks>
/// Contrat porté par <c>Validation.Contracts</c> (la règle dépend de l'abstraction, pas du module
/// Documents — frontière Contracts-only, module-rules.md §3 / CLAUDE.md n°14). La recherche est
/// TENANT-SCOPÉE par <paramref name="companyId"/> : un avoir ne peut jamais être rattaché à la facture
/// d'un autre tenant (CLAUDE.md n°9).
/// </remarks>
public interface IIssuedInvoiceLookup
{
    /// <summary>
    /// Détermine l'état de la facture d'origine référencée, dans le périmètre du tenant.
    /// </summary>
    /// <param name="companyId">Tenant propriétaire de l'avoir (clé d'isolation).</param>
    /// <param name="originalReference">Référence à la facture d'origine (numéro + date — EN 16931 BT-25).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>
    /// <see cref="OriginalInvoiceStatus.KnownIssued"/> si la facture d'origine est connue et déjà émise,
    /// <see cref="OriginalInvoiceStatus.KnownNotIssued"/> si connue mais pas encore émise,
    /// <see cref="OriginalInvoiceStatus.Unknown"/> si inconnue de la plateforme (avoir orphelin).
    /// </returns>
    Task<OriginalInvoiceStatus> FindOriginalStatusAsync(
        Guid companyId,
        PivotDocumentRefDto originalReference,
        CancellationToken cancellationToken = default);
}
