namespace Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Clé d'un document candidat à la bascule TACITE pour un purpose (ADR-0028 §4). Pré-filtre du balayage d'un
/// job système : tentative la plus récente <c>PendingValidation</c>, <c>deadline_utc</c> non null et échue. Le
/// service re-vérifie l'éligibilité SOUS VERROU avant de transiter (anti-TOCTOU). Scopé par <c>company_id</c>
/// (porté par la ligne ; sert au verrou ciblé) — base du tenant courant (CLAUDE.md n°9, jamais cross-tenant).
/// </summary>
public sealed record TacitDueDocumentDto
{
    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Document concerné.</summary>
    public required Guid DocumentId { get; init; }
}
