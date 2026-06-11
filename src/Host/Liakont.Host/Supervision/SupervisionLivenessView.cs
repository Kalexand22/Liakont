namespace Liakont.Host.Supervision;

using System;

/// <summary>Témoin de vie de la supervision : dernière évaluation du dead-man's-switch et verdict de santé.</summary>
public sealed record SupervisionLivenessView
{
    /// <summary>Horodatage UTC de la dernière évaluation réussie, ou <c>null</c> si jamais évaluée / indéterminé.</summary>
    public DateTimeOffset? LastEvaluationUtc { get; init; }

    /// <summary>Verdict de santé du dispositif.</summary>
    public required SupervisionLivenessStatus Status { get; init; }

    /// <summary>Cadence d'évaluation attendue, en minutes (F12 §5.1 : 15).</summary>
    public required int IntervalMinutes { get; init; }
}
