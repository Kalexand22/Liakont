namespace Liakont.Modules.Mandats.Application;

using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Entrée du journal append-only des transitions d'acceptation (<c>self_billed_acceptance_log</c>,
/// INV-ACCEPT-5). Écrite EN BASE dans la MÊME transaction que la transition d'état qu'elle décrit
/// (atomicité, ADR-0024 §6) : jamais la transition sans son entrée, jamais l'inverse. Immuable côté base
/// (aucun chemin d'update/delete — même discipline que <c>mandat_change_log</c> et <c>DocumentEvent</c>,
/// CLAUDE.md n°4).
/// </summary>
public sealed record SelfBilledAcceptanceLogEntry
{
    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-MANDATS-1).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Document self-billed concerné.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>État « avant » la transition ; <c>null</c> pour la genèse (création de l'agrégat).</summary>
    public SelfBilledAcceptanceState? FromState { get; init; }

    /// <summary>État « après » la transition.</summary>
    public required SelfBilledAcceptanceState ToState { get; init; }

    /// <summary>Opérateur auteur de la transition ; <c>null</c> pour une transition <b>système</b> (bascule tacite par job, MND04).</summary>
    public Guid? OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur ou de l'origine système (aide à la lecture du journal), facultatif.</summary>
    public string? OperatorName { get; init; }
}
