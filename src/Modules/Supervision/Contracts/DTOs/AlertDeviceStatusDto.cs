namespace Liakont.Modules.Supervision.Contracts.DTOs;

using System.Collections.Generic;

/// <summary>
/// État complet du dispositif d'alerte de supervision du tenant courant, pour restitution dans la console
/// (FIX210, F12 §5). Réunit les règles (actives + gelées, F12 §5.2) avec leur seuil effectif, l'état de
/// l'e-mail de l'opérateur d'instance (F12 §5.3, sans jamais exposer l'adresse) et la cadence d'évaluation
/// (F12 §5.1). Lecture seule, tenant-scopée.
/// </summary>
public record AlertDeviceStatusDto
{
    /// <summary>Les règles du dispositif (F12 §5.2), actives et gelées, dans l'ordre de la spec.</summary>
    public required IReadOnlyList<AlertRuleStatusDto> Rules { get; init; }

    /// <summary>
    /// <c>true</c> si l'e-mail de l'opérateur d'instance est configuré (F12 §5.3) — l'adresse elle-même
    /// n'est jamais exposée. <c>false</c> ⇒ les alertes restent visibles au dashboard, mais aucun e-mail
    /// opérateur n'est envoyé.
    /// </summary>
    public required bool OperatorEmailConfigured { get; init; }

    /// <summary>Cadence d'évaluation du dead-man's-switch en minutes (F12 §5.1 : toutes les 15 min).</summary>
    public required int EvaluationIntervalMinutes { get; init; }
}
