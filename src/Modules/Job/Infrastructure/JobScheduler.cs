namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Queries;
using Stratum.Modules.Job.Domain.Entities;

/// <summary>
/// Background service that polls job.schedules for active schedules whose
/// next_run_at has passed, enqueues the corresponding job, and updates next_run_at.
/// </summary>
internal sealed partial class JobScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobSchedulerOptions _options;
    private readonly ILogger<JobScheduler> _logger;

    public JobScheduler(
        IServiceScopeFactory scopeFactory,
        IOptions<JobSchedulerOptions> options,
        ILogger<JobScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogSchedulerStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSchedulerError(_logger, ex);
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }

        LogSchedulerStopped(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job scheduler started")]
    private static partial void LogSchedulerStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job scheduler stopped")]
    private static partial void LogSchedulerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during schedule polling cycle")]
    private static partial void LogSchedulerError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {ScheduleId} ({ScheduleName}) enqueued job type {JobType}")]
    private static partial void LogScheduleEnqueued(ILogger logger, Guid scheduleId, string scheduleName, string jobType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to enqueue schedule {ScheduleId} ({ScheduleName})")]
    private static partial void LogScheduleEnqueueFailed(ILogger logger, Guid scheduleId, string scheduleName, Exception exception);

    // Liakont addition (RDL08, A6-scale-2) : enqueue dé-dupliqué (un job du même type est déjà en attente).
    [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {ScheduleId} ({ScheduleName}) enqueue SUPPRESSED: a Pending job of type {JobType} already exists (anti-overlap, RDL08)")]
    private static partial void LogScheduleEnqueueSuppressed(ILogger logger, Guid scheduleId, string scheduleName, string jobType);

    private async Task ProcessDueSchedulesAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var scheduleUowFactory = scope.ServiceProvider.GetRequiredService<IScheduleUnitOfWorkFactory>();
        var jobUowFactory = scope.ServiceProvider.GetRequiredService<IJobUnitOfWorkFactory>();

        // Liakont addition (RDL08, A6-scale-2) : garde de dé-duplication à l'enqueue.
        var enqueueGuard = scope.ServiceProvider.GetService<IRecurringJobEnqueueGuard>();
        var jobQueries = scope.ServiceProvider.GetService<IJobQueries>();

        await using var scheduleUow = await scheduleUowFactory.BeginAsync(ct);
        var dueSchedules = await scheduleUow.GetDueSchedulesAsync(DateTimeOffset.UtcNow, ct);

        foreach (var schedule in dueSchedules)
        {
            try
            {
                // Liakont addition (RDL08, A6-scale-2) : ne pas empiler un déclencheur identique si un job du
                // même type/portée est déjà EN ATTENTE (sinon un fan-out plus long que la cadence cron affame
                // le worker mono-job). On avance tout de même next_run_at à la prochaine échéance cron pour
                // respecter la cadence, sans ré-essayer en boucle. Pending-only (pas Running) : voir ADR-0006 §5.
                if (enqueueGuard is not null && jobQueries is not null
                    && await enqueueGuard.ShouldSuppressEnqueueAsync(schedule.JobType, schedule.CompanyId, ct))
                {
                    var suppressedNextRunAt = CronParser.CalculateNextRun(schedule.CronExpression, DateTimeOffset.UtcNow);
                    schedule.MarkExecuted(suppressedNextRunAt);
                    await scheduleUow.UpdateScheduleAsync(schedule, ct);

                    LogScheduleEnqueueSuppressed(_logger, schedule.Id, schedule.Name, schedule.JobType);
                    continue;
                }

                var job = JobEntry.Create(
                    schedule.JobType,
                    schedule.PayloadTemplate,
                    companyId: schedule.CompanyId);

                await using var jobUow = await jobUowFactory.BeginAsync(ct);
                await jobUow.InsertJobAsync(job, ct);
                await jobUow.CommitAsync(ct);

                var nextRunAt = CronParser.CalculateNextRun(schedule.CronExpression, DateTimeOffset.UtcNow);
                schedule.MarkExecuted(nextRunAt);
                await scheduleUow.UpdateScheduleAsync(schedule, ct);

                LogScheduleEnqueued(_logger, schedule.Id, schedule.Name, schedule.JobType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogScheduleEnqueueFailed(_logger, schedule.Id, schedule.Name, ex);
            }
        }

        await scheduleUow.CommitAsync(ct);
    }
}
