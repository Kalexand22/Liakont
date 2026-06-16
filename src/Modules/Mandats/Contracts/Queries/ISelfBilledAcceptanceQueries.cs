namespace Liakont.Modules.Mandats.Contracts.Queries;

using Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Lectures (seules) de l'acceptation des auto-factures sous mandat, exposées à la frontière Contracts
/// (module-rules §3, INV-MANDATS-2). Toutes les méthodes sont scopées par <paramref name="companyId"/>
/// (jamais de lecture cross-tenant — CLAUDE.md n°9/17, INV-MANDATS-1) ; le <c>company_id</c> est résolu par
/// l'appelant via <c>ICompanyFilter.GetRequiredCompanyId</c>, jamais fourni par le client.
/// </summary>
public interface ISelfBilledAcceptanceQueries
{
    /// <summary>Charge l'acceptation d'un document ; <c>null</c> si absente pour ce tenant.</summary>
    Task<SelfBilledAcceptanceDto?> GetAcceptance(Guid companyId, Guid documentId, CancellationToken ct = default);

    /// <summary>Lit le journal append-only des transitions d'un document, du plus récent au plus ancien (audit).</summary>
    Task<IReadOnlyList<SelfBilledAcceptanceLogEntryDto>> GetAcceptanceLog(Guid companyId, Guid documentId, CancellationToken ct = default);
}
