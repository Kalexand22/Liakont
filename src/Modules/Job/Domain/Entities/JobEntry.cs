namespace Stratum.Modules.Job.Domain.Entities;

using Stratum.Modules.Job.Domain.ValueObjects;

public sealed class JobEntry
{
    private JobEntry()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public JobStatus Status { get; private set; } = null!;

    public int Priority { get; private set; }

    public int MaxRetries { get; private set; }

    public int RetryCount { get; private set; }

    public DateTimeOffset ScheduledAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Guid? CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static JobEntry Create(
        string type,
        string payload,
        int priority = 0,
        int maxRetries = 3,
        DateTimeOffset? scheduledAt = null,
        Guid? companyId = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("INV-JOB-001: Job type must not be empty.", nameof(type));
        }

        return new JobEntry
        {
            Id = Guid.NewGuid(),
            Type = type.Trim(),
            Payload = payload ?? throw new ArgumentNullException(nameof(payload)),
            Status = JobStatus.Pending,
            Priority = priority,
            MaxRetries = maxRetries,
            RetryCount = 0,
            ScheduledAt = scheduledAt ?? DateTimeOffset.UtcNow,
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static JobEntry Reconstitute(
        Guid id,
        string type,
        string payload,
        string status,
        int priority,
        int maxRetries,
        int retryCount,
        DateTimeOffset scheduledAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? errorMessage,
        Guid? companyId,
        DateTimeOffset createdAt)
    {
        return new JobEntry
        {
            Id = id,
            Type = type,
            Payload = payload,
            Status = JobStatus.From(status),
            Priority = priority,
            MaxRetries = maxRetries,
            RetryCount = retryCount,
            ScheduledAt = scheduledAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ErrorMessage = errorMessage,
            CompanyId = companyId,
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// INV-JOB-002: Only Pending -> Running is valid for starting a job.
    /// </summary>
    public void MarkRunning()
    {
        AssertTransition(JobStatus.Pending, JobStatus.Running);
        Status = JobStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// INV-JOB-002: Only Running -> Completed is valid.
    /// </summary>
    public void MarkCompleted()
    {
        AssertTransition(JobStatus.Running, JobStatus.Completed);
        Status = JobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// INV-JOB-002: Only Running -> Failed or Running -> Dead is valid.
    /// INV-JOB-003: Dead jobs are not retried.
    /// If retry_count &lt; max_retries, transitions to Pending for retry.
    /// Otherwise, transitions to Dead.
    /// </summary>
    public void MarkFailed(string errorMessage)
    {
        AssertTransition(JobStatus.Running, JobStatus.Failed);
        ErrorMessage = errorMessage;
        RetryCount++;

        if (RetryCount >= MaxRetries)
        {
            Status = JobStatus.Dead;
        }
        else
        {
            Status = JobStatus.Pending;
        }
    }

    private void AssertTransition(JobStatus expectedFrom, JobStatus to)
    {
        if (!Status.Equals(expectedFrom))
        {
            throw new InvalidOperationException(
                $"INV-JOB-002: Cannot transition from '{Status.Value}' to '{to.Value}'. " +
                $"Expected current status '{expectedFrom.Value}'.");
        }
    }
}
