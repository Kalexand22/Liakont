namespace Liakont.Modules.Supervision.Contracts.DTOs;

using System;

/// <summary>
/// Vue d'une alerte de supervision pour la console (dashboard SUP02) : un déclenchement de règle pour un
/// tenant, son état de résolution et son acquittement. La gravité est restituée en texte (fidélité à la
/// base, comme les autres lectures du produit) — l'UI mappe vers 🔴 Critique / 🟠 Avertissement (F12 §5).
/// </summary>
public sealed class AlertDto
{
    /// <summary>Identifiant de l'alerte.</summary>
    public Guid Id { get; init; }

    /// <summary>Tenant concerné (slug) — la supervision est le SEUL contexte cross-tenant du produit (lecture).</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Clé stable de la règle qui a déclenché l'alerte (ex. <c>agent.mute</c>).</summary>
    public string RuleKey { get; init; } = string.Empty;

    /// <summary>Gravité en texte (<c>Critical</c> / <c>Warning</c>).</summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>Message opérateur actionnable (français), ou <c>null</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>Horodatage de déclenchement (UTC).</summary>
    public DateTimeOffset TriggeredUtc { get; init; }

    /// <summary>Horodatage d'auto-résolution (UTC), ou <c>null</c> tant que l'alerte est active.</summary>
    public DateTimeOffset? ResolvedUtc { get; init; }

    /// <summary>Identité de l'opérateur ayant acquitté l'alerte, ou <c>null</c>.</summary>
    public string? AcknowledgedBy { get; init; }

    /// <summary>Horodatage d'acquittement (UTC), ou <c>null</c>.</summary>
    public DateTimeOffset? AcknowledgedUtc { get; init; }

    /// <summary>Vrai tant que l'alerte n'est pas résolue (condition toujours présente).</summary>
    public bool IsActive => ResolvedUtc is null;
}
