namespace Liakont.Modules.Validation.Domain;

using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Exécute toutes les règles enregistrées sur un document et agrège leurs anomalies en un
/// <see cref="ValidationResult"/> (F04 §1). Garantie « jamais de règle silencieuse » : une règle
/// qui lève une exception produit une anomalie BLOQUANTE <see cref="ValidationIssueCodes.RuleCrashed"/>
/// — jamais un passage en succès (CLAUDE.md n°3). Le module détecte, il ne corrige jamais les données.
/// </summary>
public sealed class ValidationPipeline
{
    private readonly IReadOnlyList<IDocumentRule> _rules;

    /// <summary>Crée un pipeline sur l'ensemble des règles enregistrées (injectées par le conteneur).</summary>
    /// <param name="rules">Les règles à exécuter. Jamais <c>null</c> (peut être vide).</param>
    public ValidationPipeline(IEnumerable<IDocumentRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToList();
    }

    /// <summary>
    /// Exécute chaque règle séquentiellement et agrège les anomalies. Une règle qui échoue
    /// (exception) n'interrompt pas les autres : elle est convertie en anomalie bloquante
    /// <see cref="ValidationIssueCodes.RuleCrashed"/>. Le résultat est invalide dès qu'une anomalie
    /// bloquante est présente.
    /// </summary>
    /// <param name="context">Document à valider + contexte tenant.</param>
    /// <param name="cancellationToken">Jeton d'annulation (propagé, jamais converti en RULE_CRASHED).</param>
    /// <returns>Le résultat agrégé de la validation.</returns>
    public async Task<ValidationResult> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ValidationIssue>();

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var ruleIssues = await rule.ValidateAsync(context, cancellationToken);
                if (ruleIssues is { Count: > 0 })
                {
                    issues.AddRange(ruleIssues);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                issues.Add(CreateRuleCrashedIssue(rule, context, ex));
            }
        }

        return new ValidationResult(issues);
    }

    private static ValidationIssue CreateRuleCrashedIssue(IDocumentRule rule, DocumentValidationContext context, Exception ex)
    {
        var ruleCode = string.IsNullOrWhiteSpace(rule.Code) ? rule.GetType().Name : rule.Code;
        var message =
            $"Un contrôle de conformité n'a pas pu s'exécuter (règle « {ruleCode} ») sur le document n° {context.Document.Number}. " +
            "Le document reste bloqué par précaution. Contactez le support Liakont en indiquant ce numéro de document.";
        var detail = $"La règle '{ruleCode}' a levé {ex.GetType().FullName} : {ex.Message}";
        return ValidationIssue.Blocking(ValidationIssueCodes.RuleCrashed, message, detail);
    }
}
