namespace Liakont.Modules.Pipeline.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain;

/// <summary>
/// Écriture du journal d'exécutions du pipeline (<c>pipeline.run_logs</c>) — pendant écriture de
/// <see cref="Contracts.Queries.IPipelineRunQueries"/> (lecture, PIP01a). Chaque exécution du pipeline
/// (CHECK/SEND/SYNC) consigne UNE trace clôturée (PIP01b+). L'écriture est TENANT-SCOPÉE : la connexion
/// EST le tenant (database-per-tenant, blueprint §7) — aucune trace cross-tenant n'est possible.
/// </summary>
public interface IPipelineRunLogStore
{
    /// <summary>
    /// Persiste une exécution clôturée (<see cref="RunLog.IsCompleted"/>) dans la base du tenant courant.
    /// </summary>
    /// <param name="runLog">L'exécution à consigner (clôturée).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task SaveAsync(RunLog runLog, CancellationToken cancellationToken = default);
}
