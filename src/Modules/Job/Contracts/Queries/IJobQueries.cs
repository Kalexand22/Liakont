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

    // Liakont addition (RDL08) : dé-duplication à l'enqueue des jobs récurrents (A6-scale-2). Vrai s'il existe
    // déjà un job du type donné EN ATTENTE (status 'Pending') pour la même portée tenant (company_id NULL pour
    // les jobs système). Volontairement limité à 'Pending' (pas 'Running') : dé-duper contre 'Running'
    // bloquerait à jamais un job dont l'entrée Running a été orpheline par un crash (aucun reaper, A6-scale-1).
    // Lecture seule, additive. Voir docs/adr/ADR-0006 §5.
    Task<bool> HasPendingJobOfTypeAsync(string jobType, Guid? companyId, CancellationToken ct = default);
}
