namespace Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Vue de lecture (seule) d'une entrée du journal append-only des modifications de mandants/mandats
/// (<c>mandat_change_log</c>, INV-MANDATS-3). DTO pur. Le journal est immuable (aucun chemin
/// d'update/delete, CLAUDE.md n°4) ; ce DTO ne sert qu'à l'audit.
/// </summary>
public sealed record MandatChangeLogEntryDto
{
    /// <summary>Mandant concerné par la modification.</summary>
    public required Guid MandantId { get; init; }

    /// <summary>Mandat concerné ; <c>null</c> pour une modification de mandant (registre).</summary>
    public Guid? MandatId { get; init; }

    /// <summary>Référence métier de l'entité touchée (mandant ou mandat).</summary>
    public required string Reference { get; init; }

    /// <summary>Nature de la modification (nom de <c>MandatChangeType</c>).</summary>
    public required string ChangeType { get; init; }

    /// <summary>Valeur « avant » sérialisée en JSON ; <c>null</c> pour une création.</summary>
    public string? BeforeValue { get; init; }

    /// <summary>Valeur « après » sérialisée en JSON ; <c>null</c> pour une révocation/suppression.</summary>
    public string? AfterValue { get; init; }

    /// <summary>Identité de l'opérateur auteur de la modification.</summary>
    public required Guid OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur (aide à la lecture), facultatif.</summary>
    public string? OperatorName { get; init; }

    /// <summary>Horodatage de la modification (UTC).</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
