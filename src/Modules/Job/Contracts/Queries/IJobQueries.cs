namespace Stratum.Modules.Job.Contracts.Queries;

using Stratum.Modules.Job.Contracts.DTOs;

public interface IJobQueries
{
    Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default);

    Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default);

    // Liakont addition (FIX210) : lecture ciblée du dernier achèvement d'un type de job, sans scan plafonné.
    // Le témoin de vie de la supervision (dead-man's-switch, F12 §5.1) a besoin de la dernière exécution
    // réussie d'UN type précis ; filtrer en SQL par type évite qu'un volume de jobs d'autres types ne pousse
    // l'exécution recherchée hors d'une fenêtre LIMIT (faux « jamais évalué »). Lecture seule, additive.
    Task<DateTimeOffset?> GetLastCompletedAtByTypeAsync(string jobType, CancellationToken ct = default);
}
