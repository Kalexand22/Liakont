namespace Liakont.Modules.Supervision.Application;

/// <summary>Échec d'évaluation d'une règle pendant un cycle : sa clé et le message d'erreur.</summary>
public sealed record RuleEvaluationFailure(string RuleKey, string ErrorMessage);
