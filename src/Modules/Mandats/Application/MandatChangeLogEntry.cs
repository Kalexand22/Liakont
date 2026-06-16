namespace Liakont.Modules.Mandats.Application;

using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Entrée du journal append-only des modifications de mandants/mandats (<c>mandat_change_log</c>,
/// INV-MANDATS-3). Écrite EN BASE dans la MÊME transaction que la mutation qu'elle décrit (atomicité,
/// ADR-0022 §3) : jamais la mutation sans son entrée, jamais l'inverse. Immuable côté base (aucun chemin
/// d'update/delete — même discipline que <c>DocumentEvent</c> et <c>mapping_change_log</c>, CLAUDE.md n°4).
/// Les valeurs avant/après sont sérialisées en JSON par l'infrastructure (<c>MandatChangeLogFactory</c>).
/// </summary>
public sealed record MandatChangeLogEntry
{
    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-MANDATS-1).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Mandant concerné par la modification.</summary>
    public required Guid MandantId { get; init; }

    /// <summary>Mandat concerné ; <c>null</c> pour une modification de mandant (registre).</summary>
    public Guid? MandatId { get; init; }

    /// <summary>Référence métier de l'entité touchée (mandant ou mandat) — aide à la lecture du journal.</summary>
    public required string Reference { get; init; }

    /// <summary>Nature de la modification.</summary>
    public required MandatChangeType ChangeType { get; init; }

    /// <summary>Valeur « avant » sérialisée en JSON ; <c>null</c> pour une création.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>Valeur « après » sérialisée en JSON ; <c>null</c> pour une révocation/suppression.</summary>
    public string? AfterJson { get; init; }

    /// <summary>Identité de l'opérateur auteur de la modification.</summary>
    public required Guid OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur (aide à la lecture du journal), facultatif.</summary>
    public string? OperatorName { get; init; }
}
