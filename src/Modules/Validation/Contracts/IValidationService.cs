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
}
