namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Règle factice contrôlable : son déclenchement est piloté par <see cref="IsFiring"/>, et elle peut
/// simuler une panne (<see cref="ThrowOnEvaluate"/>) pour vérifier l'isolation d'une règle buguée.
/// </summary>
internal sealed class FakeAlertRule : IAlertRule
{
    public FakeAlertRule(string ruleKey, AlertSeverity severity = AlertSeverity.Warning, bool isFiring = false)
    {
        RuleKey = ruleKey;
        Severity = severity;
        IsFiring = isFiring;
    }

    public string RuleKey { get; }

    public AlertSeverity Severity { get; }

    /// <summary>Condition courante de la règle (modifiable entre cycles).</summary>
    public bool IsFiring { get; set; }

    /// <summary>Message porté par l'alerte quand la règle se déclenche.</summary>
    public string? Detail { get; set; }

    /// <summary>Si vrai, l'évaluation lève (simulation d'une règle buguée).</summary>
    public bool ThrowOnEvaluate { get; set; }

    /// <summary>Nombre d'évaluations subies (anti-bruit observable).</summary>
    public int EvaluationCount { get; private set; }

    public Task<AlertEvaluation> EvaluateAsync(AlertEvaluationContext context, CancellationToken cancellationToken = default)
    {
        EvaluationCount++;

        if (ThrowOnEvaluate)
        {
            throw new InvalidOperationException($"Règle {RuleKey} en panne (simulation).");
        }

        return Task.FromResult(IsFiring ? AlertEvaluation.Firing(Detail) : AlertEvaluation.Clear());
    }
}
