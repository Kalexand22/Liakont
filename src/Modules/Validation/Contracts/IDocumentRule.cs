namespace Liakont.Modules.Validation.Contracts;

/// <summary>
/// Une règle de validation documentaire (F04). Implémentée uniquement par les règles métier du
/// module Validation (VAL02-VAL05) : toute la logique de validation vit sur la plateforme, jamais
/// dans l'agent (CLAUDE.md n°6). Une règle DÉTECTE, elle ne corrige jamais les données. En
/// fonctionnement nominal elle ne lève pas d'exception : une exception est convertie par le
/// pipeline en anomalie bloquante <c>RULE_CRASHED</c> (jamais un passage silencieux).
/// </summary>
public interface IDocumentRule
{
    /// <summary>Code stable identifiant la règle (attribution d'un <c>RULE_CRASHED</c>, audit).</summary>
    string Code { get; }

    /// <summary>
    /// Évalue le document et retourne les anomalies détectées (liste vide si conforme à cette règle).
    /// Asynchrone : une règle peut interroger un autre module via ses Contracts (unicité du numéro
    /// via Documents, couverture du mapping via TvaMapping — VAL03/VAL04).
    /// </summary>
    /// <param name="context">Document à valider + contexte tenant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Les anomalies détectées (vide si le document est conforme à cette règle).</returns>
    Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default);
}
