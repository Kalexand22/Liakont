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
    /// Vrai si la règle a besoin du résultat du MAPPING TVA — la catégorie UNCL5305 et le code VATEX posés sur
    /// les ventilations par l'enrichissement du CHECK (<c>PivotLineTaxDto.CategoryCode</c> / <c>VatexCode</c>).
    /// Une telle règle ne peut s'évaluer utilement que sur un document DÉJÀ MAPPÉ ; elle est donc EXCLUE de
    /// l'agrégation des motifs indépendants du mapping (FIX06,
    /// <see cref="IValidationService.ValidateMappingIndependentAsync"/>), qui sert à montrer dès le premier
    /// CHECK tout ce qui est corrigeable SANS la table TVA. Par défaut <c>false</c> : la grande majorité des
    /// règles (forme, identité, arithmétique, garde-fou B2B/B2C) ne lisent que des champs préservés par
    /// l'enrichissement et restent évaluables avant le mapping. SEULES les règles qui lisent la catégorie ou le
    /// VATEX surchargent à <c>true</c>. Conséquence d'un oubli : la règle serait évaluée tôt sur un document non
    /// enrichi — AU PIRE un motif redondant (on montre PLUS, jamais MOINS — décision D5), JAMAIS un document
    /// laissé passer (l'agrégation ne s'exécute que sur un document DÉJÀ bloqué par le mapping).
    /// </summary>
    bool DependsOnTvaMapping => false;

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
