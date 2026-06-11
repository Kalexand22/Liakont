namespace Liakont.Modules.Validation.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Surface inter-modules du module Validation (frontière Contracts-only, module-rules §3) : exécute le
/// pipeline de règles (F04) sur un document et agrège les anomalies en un <see cref="ValidationResult"/>.
/// Consommé par le pipeline (PIP01b, CHECK). Le module DÉTECTE, il ne corrige jamais les données
/// (module-rules §2) ; une règle qui échoue devient une anomalie bloquante, jamais un succès silencieux.
/// </summary>
public interface IValidationService
{
    /// <summary>Valide un document (modèle pivot + contexte tenant) et retourne le résultat agrégé.</summary>
    /// <param name="context">Document à valider + identité du tenant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le résultat agrégé de la validation (bloquant dès qu'une anomalie bloquante est présente).</returns>
    Task<ValidationResult> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide un document en n'exécutant QUE les règles INDÉPENDANTES du mapping TVA (celles dont
    /// <see cref="IDocumentRule.DependsOnTvaMapping"/> est faux : garde-fou B2B/B2C, contrôles de forme,
    /// d'identité et d'arithmétique). Le CHECK l'utilise pour AGRÉGER ces motifs au blocage de mapping (FIX06) :
    /// quand le mapping échoue (table absente ou régime non couvert), l'opérateur voit dès le PREMIER CHECK
    /// tout ce qui est corrigeable indépendamment de la table TVA, au lieu de découvrir les couches une par une
    /// à coups de « Revérifier ». N'AFFAIBLIT aucune validation (module-rules §2, CLAUDE.md n°3) : la validation
    /// COMPLÈTE — toutes les règles, y compris celles qui dépendent du mapping — reste exécutée par
    /// <see cref="ValidateAsync"/> dès que le mapping aboutit. C'est une vue PARTIELLE et ANTICIPÉE, jamais un
    /// substitut.
    /// </summary>
    /// <param name="context">Document à valider (pivot NON enrichi : les règles retenues n'en ont pas besoin) + identité du tenant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le résultat agrégé des seules règles indépendantes du mapping.</returns>
    Task<ValidationResult> ValidateMappingIndependentAsync(DocumentValidationContext context, CancellationToken cancellationToken = default);
}
