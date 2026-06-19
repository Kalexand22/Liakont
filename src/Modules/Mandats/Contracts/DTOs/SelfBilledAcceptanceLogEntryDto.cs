namespace Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Vue de lecture (seule) d'une entrée du journal append-only des transitions d'acceptation
/// (<c>self_billed_acceptance_log</c>, INV-ACCEPT-5). DTO pur. Le journal est immuable (aucun chemin
/// d'update/delete, CLAUDE.md n°4) ; ce DTO ne sert qu'à l'audit. Les états sont exposés sous forme de
/// <b>nom</b> (et non l'enum de domaine) pour garder la surface <c>Contracts</c> sans dépendance sur <c>Domain</c>.
/// </summary>
public sealed record SelfBilledAcceptanceLogEntryDto
{
    /// <summary>Document self-billed concerné.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>État « avant » (nom de <c>SelfBilledAcceptanceState</c>) ; <c>null</c> pour la genèse (création).</summary>
    public string? FromState { get; init; }

    /// <summary>État « après » (nom de <c>SelfBilledAcceptanceState</c>).</summary>
    public required string ToState { get; init; }

    /// <summary>Opérateur auteur de la transition ; <c>null</c> pour une transition système (bascule tacite).</summary>
    public Guid? OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur ou de l'origine système (aide à la lecture), facultatif.</summary>
    public string? OperatorName { get; init; }

    /// <summary>Horodatage de la transition (UTC).</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
