namespace Liakont.Modules.Supervision.Application;

using System.Collections.Generic;

/// <summary>
/// Bilan d'un cycle d'évaluation pour UN tenant : nombre de règles évaluées avec succès et échecs par
/// règle. Le moteur ISOLE l'échec d'une règle (une règle buguée ne doit pas masquer les autres) ; les
/// échecs sont remontés ici pour que l'appelant (le job tenant) les signale au runner / à la supervision —
/// jamais avalés silencieusement (un échec silencieux d'une règle = panne silencieuse, l'inverse du but).
/// </summary>
public sealed class AlertEvaluationResult
{
    public AlertEvaluationResult(int rulesEvaluated, IReadOnlyList<RuleEvaluationFailure> failures)
    {
        RulesEvaluated = rulesEvaluated;
        Failures = failures;
    }

    /// <summary>Nombre de règles évaluées sans erreur durant le cycle.</summary>
    public int RulesEvaluated { get; }

    /// <summary>Échecs d'évaluation, par règle (vide si toutes les règles ont été évaluées).</summary>
    public IReadOnlyList<RuleEvaluationFailure> Failures { get; }

    /// <summary>Vrai si au moins une règle a échoué durant le cycle.</summary>
    public bool HasFailures => Failures.Count > 0;
}
