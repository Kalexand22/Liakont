namespace Stratum.Modules.Job.Application;

using Stratum.Modules.Job.Domain.Entities;

public interface IScheduleUnitOfWork : IAsyncDisposable
{
    Task InsertScheduleAsync(JobSchedule schedule, CancellationToken ct = default);

    Task UpdateScheduleAsync(JobSchedule schedule, CancellationToken ct = default);

    Task<JobSchedule?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken ct = default);

    Task<bool> ExistsByNameAndCompanyAsync(string name, Guid companyId, Guid? excludeId = null, CancellationToken ct = default);

    Task<IReadOnlyList<JobSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default);

    // Liakont addition (FIX203b) : lecture seule, sans verrou, des types de jobs ayant au moins un
    // schedule ACTIF — consommée par le diagnostic de démarrage qui avertit si un job SYSTÈME attendu
    // (évaluation de la supervision, ancrage quotidien du coffre) n'a aucune planification active.
    // Distincte de GetDueSchedulesAsync (FOR UPDATE SKIP LOCKED, réservée au scheduler).
    Task<IReadOnlyList<string>> GetActiveJobTypesAsync(CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);
}

public interface IScheduleUnitOfWorkFactory
{
    Task<IScheduleUnitOfWork> BeginAsync(CancellationToken ct = default);
}
