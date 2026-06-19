namespace Liakont.Modules.DocumentApproval.Application;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// Entrée du journal append-only des transitions de validation (<c>document_approval_log</c>, ADR-0028 §7,
/// INV-APPROVAL-6). Écrite EN BASE dans la MÊME transaction que la transition qu'elle décrit (atomicité :
/// jamais la transition sans son entrée, jamais l'inverse). Immuable côté base (double trigger — même
/// discipline que <c>self_billed_acceptance_log</c>/<c>DocumentEvent</c>, CLAUDE.md n°4).
/// </summary>
public sealed record DocumentApprovalLogEntry
{
    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Document concerné.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Finalité de validation.</summary>
    public required ValidationPurpose Purpose { get; init; }

    /// <summary>Tentative concernée.</summary>
    public required int Attempt { get; init; }

    /// <summary>État « avant » la transition ; <c>null</c> pour la genèse (création de la tentative).</summary>
    public ValidationState? FromState { get; init; }

    /// <summary>État « après » la transition.</summary>
    public required ValidationState ToState { get; init; }

    /// <summary>Slot concerné (N-parties) ; <c>null</c> pour une transition d'agrégat.</summary>
    public string? SignerId { get; init; }

    /// <summary>Opérateur auteur ; <c>null</c> pour une transition SYSTÈME (bascule tacite / timeout par job).</summary>
    public Guid? OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur ou de l'origine système (aide à la lecture du journal), facultatif.</summary>
    public string? OperatorName { get; init; }
}
