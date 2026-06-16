namespace Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Vue de lecture (seule) de l'acceptation d'une auto-facture sous mandat (ADR-0024, F15 §2.2/§2.3). DTO pur
/// (aucune logique). <see cref="State"/> est exposé sous forme de <b>nom</b> (et non l'enum de domaine) pour
/// garder la surface <c>Contracts</c> sans dépendance sur <c>Domain</c>. <see cref="IsAccepted"/> expose l'état
/// calculé « le gate d'émission est ouvert » (Accepted ou TacitlyAccepted) sans dupliquer la règle.
/// </summary>
public sealed record SelfBilledAcceptanceDto
{
    /// <summary>Document self-billed concerné.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>État courant de l'acceptation (nom de <c>SelfBilledAcceptanceState</c>).</summary>
    public required string State { get; init; }

    /// <summary>BT-1 fiscal alloué par mandant (MND05/ADR-0025) ; <c>null</c> tant que non alloué.</summary>
    public string? AllocatedNumber { get; init; }

    /// <summary>Instant (UTC) d'entrée en attente d'acceptation.</summary>
    public required DateTimeOffset PendingSince { get; init; }

    /// <summary>Échéance (UTC) de bascule tacite ; <c>null</c> = bascule tacite impossible.</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }

    /// <summary>Le gate d'émission est ouvert (état Accepted ou TacitlyAccepted) — la garde réelle est livrée par MND03.</summary>
    public required bool IsAccepted { get; init; }
}
