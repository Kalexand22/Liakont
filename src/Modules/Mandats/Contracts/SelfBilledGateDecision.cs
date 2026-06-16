namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Verdict de la garde d'émission (<see cref="ISelfBilledGate"/>, ADR-0024 §3). DTO pur (aucune logique).
/// <see cref="IsEmissionAllowed"/> = l'acceptation est ouverte (<c>Accepted</c>/<c>TacitlyAccepted</c>).
/// <see cref="AcceptanceState"/> = nom de l'état d'acceptation courant (ou <c>null</c> si AUCUN
/// enregistrement n'existe), exposé pour composer un message opérateur précis (CLAUDE.md n°12) sans dupliquer
/// la règle « gate ouvert ».
/// </summary>
public sealed record SelfBilledGateDecision
{
    /// <summary>L'émission est autorisée (acceptation <c>Accepted</c> ou <c>TacitlyAccepted</c>).</summary>
    public required bool IsEmissionAllowed { get; init; }

    /// <summary>Nom de l'état d'acceptation courant, ou <c>null</c> si aucune acceptation n'est enregistrée.</summary>
    public string? AcceptanceState { get; init; }
}
