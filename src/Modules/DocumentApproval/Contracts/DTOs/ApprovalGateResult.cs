namespace Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Verdict de la Règle de gate (ADR-0028 §5, INV-APPROVAL-4) exposé à la frontière <c>Contracts</c> pour les
/// ports de purpose (SIG06). DTO pur, sans niveau de preuve typé : <see cref="Reason"/> est un message opérateur
/// français (CLAUDE.md n°12). La Règle (état nécessaire + niveau requis tenant + forme expresse self-billing) est
/// évaluée par le Domain (<c>ApprovalGate</c>) ; ce résultat n'en est que la projection consommable.
/// </summary>
public sealed record ApprovalGateResult
{
    /// <summary>Le gate d'émission est ouvert (les trois conditions de la Règle de gate sont réunies).</summary>
    public required bool IsOpen { get; init; }

    /// <summary>Message opérateur (français) expliquant l'ouverture ou le blocage.</summary>
    public required string Reason { get; init; }
}
