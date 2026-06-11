namespace Liakont.Modules.Supervision.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Lecture de la dernière évaluation du dead-man's-switch de supervision (FIX210, F12 §5.1). Le type technique
/// du job d'évaluation et la source de ses exécutions appartiennent au module Supervision : cette abstraction
/// les garde hors de la couche appelante (le témoin de vie côté Host n'a plus à connaître l'implémentation).
/// <para>
/// L'évaluation est un job SYSTÈME (instance, hors tenant) : l'appelant ouvre un scope SYSTÈME avant d'appeler
/// (la connexion route alors vers la base système où vivent les jobs système). Aucune donnée métier tenant.
/// </para>
/// </summary>
public interface ISupervisionLivenessQueries
{
    /// <summary>Horodatage UTC de la dernière évaluation TERMINÉE du dispositif, ou <c>null</c> si jamais évaluée.</summary>
    Task<DateTimeOffset?> GetLastEvaluationUtcAsync(CancellationToken cancellationToken = default);
}
