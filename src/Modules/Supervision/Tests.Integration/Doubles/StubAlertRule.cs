namespace Liakont.Modules.Supervision.Tests.Integration.Doubles;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;

/// <summary>Règle de test au déclenchement piloté — sert à prouver le moteur sur base réelle (aucune règle produit en SUP01a).</summary>
internal sealed class StubAlertRule : IAlertRule
{
    public StubAlertRule(string ruleKey, AlertSeverity severity, bool isFiring, string? detail = null)
    {
        RuleKey = ruleKey;
        Severity = severity;
        IsFiring = isFiring;
        Detail = detail;
    }

    public string RuleKey { get; }

    public AlertSeverity Severity { get; }

    public bool IsFiring { get; set; }

    public string? Detail { get; set; }

    /// <summary>Si vrai, l'évaluation lève (simulation d'une règle buguée, pour l'isolation d'échec).</summary>
    public bool ThrowOnEvaluate { get; set; }

    public Task<AlertEvaluation> EvaluateAsync(AlertEvaluationContext context, CancellationToken cancellationToken = default)
    {
        if (ThrowOnEvaluate)
        {
            throw new InvalidOperationException($"Règle {RuleKey} en panne (simulation).");
        }

        return Task.FromResult(IsFiring ? AlertEvaluation.Firing(Detail) : AlertEvaluation.Clear());
    }
}
