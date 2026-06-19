namespace Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Vue de lecture (seule) d'une entrée du journal append-only des transitions de validation
/// (<c>document_approval_log</c>, ADR-0028 §7, INV-APPROVAL-6). DTO pur. Le journal est immuable côté base
/// (double trigger). <see cref="FromState"/>/<see cref="ToState"/> sont des noms de <c>ValidationState</c> ;
/// <see cref="FromState"/> <c>null</c> = genèse (création de la tentative).
/// </summary>
public sealed record DocumentApprovalLogEntryDto
{
    /// <summary>Document concerné.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Finalité de validation.</summary>
    public required ValidationPurpose Purpose { get; init; }

    /// <summary>Numéro de tentative concernée.</summary>
    public required int Attempt { get; init; }

    /// <summary>État « avant » (nom de <c>ValidationState</c>) ; <c>null</c> pour la genèse.</summary>
    public string? FromState { get; init; }

    /// <summary>État « après » (nom de <c>ValidationState</c>).</summary>
    public required string ToState { get; init; }

    /// <summary>Slot concerné (N-parties) ; <c>null</c> pour une transition d'agrégat.</summary>
    public string? SignerId { get; init; }

    /// <summary>Opérateur auteur ; <c>null</c> = transition SYSTÈME (bascule tacite / timeout par job).</summary>
    public Guid? OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur ou de l'origine système (facultatif).</summary>
    public string? OperatorName { get; init; }

    /// <summary>Horodatage (UTC) de la transition.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
