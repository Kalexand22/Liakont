namespace Liakont.Modules.Validation.Domain;

/// <summary>
/// Codes d'anomalie produits par le socle de validation lui-même, par opposition aux codes des
/// règles métier (VAL02-VAL05), définis par chaque règle.
/// </summary>
public static class ValidationIssueCodes
{
    /// <summary>
    /// Une règle a levé une exception inattendue : le pipeline produit cette anomalie BLOQUANTE
    /// plutôt que de laisser passer le document (F04, CLAUDE.md n°3 — jamais de règle silencieuse).
    /// </summary>
    public const string RuleCrashed = "RULE_CRASHED";
}
