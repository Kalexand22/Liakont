namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Events;
using Stratum.Modules.Job.Domain.ValueObjects;

/// <summary>
/// Background worker that polls the job.jobs table for pending jobs,
/// resolves the appropriate IJobHandler&lt;T&gt;, executes, and updates status.
/// Retry logic: on failure, retry_count is incremented. If retries remaining,
/// status returns to Pending. Otherwise, status becomes Dead.
/// </summary>
internal sealed partial class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobHandlerResolver _handlerResolver;
    private readonly JobWorkerOptions _options;
    private readonly ILogger<JobWorker> _logger;

    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IJobHandlerResolver handlerResolver,
        IOptions<JobWorkerOptions> options,
        ILogger<JobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _handlerResolver = handlerResolver;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPollingError(_logger, ex);
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }

        LogWorkerStopped(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job worker started")]
    private static partial void LogWorkerStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job worker stopped")]
    private static partial void LogWorkerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during job polling cycle")]
    private static partial void LogPollingError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Acquired job {JobId} (type: {JobType})")]
    private static partial void LogJobAcquired(ILogger logger, Guid jobId, string jobType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} (type: {JobType}) completed")]
    private static partial void LogJobCompleted(ILogger logger, Guid jobId, string jobType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} (type: {JobType}) failed")]
    private static partial void LogJobFailed(ILogger logger, Guid jobId, string jobType, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} (type: {JobType}) retrying ({RetryCount}/{MaxRetries})")]
    private static partial void LogJobRetrying(ILogger logger, Guid jobId, string jobType, int retryCount, int maxRetries);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} (type: {JobType}) dead-lettered after {MaxRetries} retries")]
    private static partial void LogJobDeadLettered(ILogger logger, Guid jobId, string jobType, int maxRetries);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mark job {JobId} as failed")]
    private static partial void LogMarkFailedError(ILogger logger, Guid jobId, Exception exception);

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var processedCount = 0;

        for (var i = 0; i < _options.BatchSize; i++)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var acquired = await TryProcessOneJobAsync(ct);
            if (!acquired)
            {
                break; // No more pending jobs
            }

            processedCount++;
        }

        return processedCount;
    }

    private async Task<bool> TryProcessOneJobAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var uowFactory = scope.ServiceProvider.GetRequiredService<IJobUnitOfWorkFactory>();

        // Phase 1: Acquire and mark running
        await using var uow = await uowFactory.BeginAsync(ct);
        var job = await uow.AcquireNextPendingJobAsync(ct);

        if (job is null)
        {
            return false;
        }

        LogJobAcquired(_logger, job.Id, job.Type);

        job.MarkRunning();
        await uow.UpdateJobAsync(job, ct);
        await uow.CommitAsync(ct);

        // Phase 2: Execute handler (outside the lock transaction)
        try
        {
            if (!_handlerResolver.CanHandle(job.Type))
            {
                throw new InvalidOperationException(
                    $"INV-JOB-001: No handler registered for job type '{job.Type}'.");
            }

            await _handlerResolver.ExecuteAsync(scope.ServiceProvider, job.Type, job.Payload, ct);

            // Phase 3a: Mark completed
            await using var completeUow = await uowFactory.BeginAsync(ct);
            var completedJob = await completeUow.GetJobByIdAsync(job.Id, ct)
                ?? throw new InvalidOperationException($"Job {job.Id} not found after execution.");
            completedJob.MarkCompleted();
            await completeUow.UpdateJobAsync(completedJob, ct);

            var completedEvent = new IntegrationEvent<JobCompletedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "job.job.completed",
                OccurredAt = completedJob.CompletedAt!.Value,
                CorrelationId = Guid.NewGuid(),
                ModuleSource = "job",
                Version = 1,
                Payload = new JobCompletedV1
                {
                    JobId = job.Id,
                    Type = job.Type,
                    CompletedAt = completedJob.CompletedAt!.Value,
                },
            };
            await completeUow.CommitWithEventAsync(completedEvent, ct);

            LogJobCompleted(_logger, job.Id, job.Type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogJobFailed(_logger, job.Id, job.Type, ex);

            try
            {
                await MarkJobFailedAsync(uowFactory, job.Id, job.Type, ex.Message, ct);
            }
            catch (Exception markEx) when (markEx is not OperationCanceledException)
            {
                LogMarkFailedError(_logger, job.Id, markEx);
            }
        }

        return true;
    }

    private async Task MarkJobFailedAsync(
        IJobUnitOfWorkFactory uowFactory,
        Guid jobId,
        string jobType,
        string errorMessage,
        CancellationToken ct)
    {
        await using var uow = await uowFactory.BeginAsync(ct);
        var job = await uow.GetJobByIdAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found for failure marking.");

        job.MarkFailed(errorMessage);
        await uow.UpdateJobAsync(job, ct);

        if (job.Status.Equals(JobStatus.Dead))
        {
            var deadEvent = new IntegrationEvent<JobDeadLetteredV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "job.job.dead_lettered",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(),
                ModuleSource = "job",
                Version = 1,
                Payload = new JobDeadLetteredV1
                {
                    JobId = jobId,
                    Type = jobType,
                    ErrorMessage = errorMessage,
                    MaxRetries = job.MaxRetries,
                },
            };
            await uow.CommitWithEventAsync(deadEvent, ct);

            LogJobDeadLettered(_logger, jobId, jobType, job.MaxRetries);
        }
        else
        {
            var failedEvent = new IntegrationEvent<JobFailedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "job.job.failed",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(),
                ModuleSource = "job",
                Version = 1,
                Payload = new JobFailedV1
                {
                    JobId = jobId,
                    Type = jobType,
                    ErrorMessage = errorMessage,
                    RetryCount = job.RetryCount,
                    MaxRetries = job.MaxRetries,
                },
            };
            await uow.CommitWithEventAsync(failedEvent, ct);

            LogJobRetrying(_logger, jobId, jobType, job.RetryCount, job.MaxRetries);
        }
    }
}
