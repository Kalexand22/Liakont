namespace Liakont.Modules.DocumentApproval.Domain;

/// <summary>
/// Verdict de la Règle de gate (<see cref="ApprovalGate"/>, ADR-0028 §5). DTO pur. <see cref="Reason"/> est un
/// message opérateur en français (CLAUDE.md n°12), composable sans parser.
/// </summary>
public sealed record ApprovalGateDecision
{
    /// <summary>Le gate d'émission est ouvert (les trois conditions de la Règle de gate sont réunies).</summary>
    public required bool IsOpen { get; init; }

    /// <summary>Message opérateur (français) expliquant l'ouverture ou le blocage.</summary>
    public required string Reason { get; init; }
}
