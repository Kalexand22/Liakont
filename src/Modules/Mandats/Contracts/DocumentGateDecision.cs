namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Verdict générique d'un port de gate de purpose (SIG06) exposé par <c>Mandats.Contracts</c> :
/// <see cref="IMandateSignatureGate"/>, <see cref="ICreditNoteAcceptanceGate"/>,
/// <see cref="IMultiPartySignatureGate"/>. <see cref="IsOpen"/> = l'émission/activation est autorisée (la Règle de
/// gate générique — ADR-0028 §5 — est satisfaite) ; <see cref="Reason"/> est un message opérateur français
/// (CLAUDE.md n°12). Distinct de <see cref="SelfBilledGateDecision"/> (qui porte en plus l'état fiscal
/// d'acceptation 389, conservé pour la non-régression du pipeline).
/// </summary>
public sealed record DocumentGateDecision
{
    /// <summary>Le gate est ouvert (les trois conditions de la Règle de gate sont réunies — ADR-0028 §5).</summary>
    public required bool IsOpen { get; init; }

    /// <summary>Message opérateur (français) expliquant l'ouverture ou le blocage.</summary>
    public required string Reason { get; init; }
}
