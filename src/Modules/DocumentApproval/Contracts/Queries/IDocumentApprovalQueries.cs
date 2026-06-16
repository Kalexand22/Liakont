namespace Liakont.Modules.DocumentApproval.Contracts.Queries;

using Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Lectures (seules) du workflow de validation de document, exposées à la frontière <c>Contracts</c>
/// (module-rules §3, CLAUDE.md n°6/14). Toutes les méthodes sont scopées par <paramref name="companyId"/>
/// (jamais de lecture cross-tenant — CLAUDE.md n°9/17, INV-APPROVAL-6) ; le <c>company_id</c> est résolu par
/// l'appelant (jamais fourni par le client).
/// </summary>
public interface IDocumentApprovalQueries
{
    /// <summary>
    /// Charge la tentative la PLUS RÉCENTE (<c>attempt</c> max, indépendamment de sa terminalité — ADR-0028 §6)
    /// d'un document pour un purpose ; <c>null</c> si aucune tentative n'existe pour ce tenant.
    /// </summary>
    Task<DocumentValidationDto?> GetLatestAttempt(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Lit le journal append-only des transitions d'un document/purpose (toutes tentatives), du plus récent au
    /// plus ancien (audit).
    /// </summary>
    Task<IReadOnlyList<DocumentApprovalLogEntryDto>> GetApprovalLog(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default);
}
