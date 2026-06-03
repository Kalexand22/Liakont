namespace Stratum.Modules.Job.Domain.Entities;

public sealed class JobSchedule
{
    private JobSchedule()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string CronExpression { get; private set; } = string.Empty;

    public string JobType { get; private set; } = string.Empty;

    public string PayloadTemplate { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset NextRunAt { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new schedule. Caller must provide a pre-validated nextRunAt
    /// (computed from the cron expression by the infrastructure layer).
    /// </summary>
    public static JobSchedule Create(
        string name,
        string cronExpression,
        string jobType,
        string payloadTemplate,
        Guid companyId,
        DateTimeOffset nextRunAt)
    {
        ValidateName(name);
        ValidateCronExpressionNotEmpty(cronExpression);

        if (string.IsNullOrWhiteSpace(jobType))
        {
            throw new ArgumentException("Job type must not be empty.", nameof(jobType));
        }

        var now = DateTimeOffset.UtcNow;

        return new JobSchedule
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CronExpression = cronExpression.Trim(),
            JobType = jobType.Trim(),
            PayloadTemplate = payloadTemplate ?? "{}",
            IsActive = true,
            NextRunAt = nextRunAt,
            LastRunAt = null,
            CompanyId = companyId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static JobSchedule Reconstitute(
        Guid id,
        string name,
        string cronExpression,
        string jobType,
        string payloadTemplate,
        bool isActive,
        DateTimeOffset nextRunAt,
        DateTimeOffset? lastRunAt,
        Guid companyId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new JobSchedule
        {
            Id = id,
            Name = name,
            CronExpression = cronExpression,
            JobType = jobType,
            PayloadTemplate = payloadTemplate,
            IsActive = isActive,
            NextRunAt = nextRunAt,
            LastRunAt = lastRunAt,
            CompanyId = companyId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// Updates the schedule. Caller must provide a pre-validated nextRunAt.
    /// </summary>
    public void Update(string name, string cronExpression, string jobType, string payloadTemplate, DateTimeOffset nextRunAt)
    {
        ValidateName(name);
        ValidateCronExpressionNotEmpty(cronExpression);

        if (string.IsNullOrWhiteSpace(jobType))
        {
            throw new ArgumentException("Job type must not be empty.", nameof(jobType));
        }

        Name = name.Trim();
        CronExpression = cronExpression.Trim();
        JobType = jobType.Trim();
        PayloadTemplate = payloadTemplate ?? "{}";
        NextRunAt = nextRunAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Toggle(DateTimeOffset? nextRunAt = null)
    {
        IsActive = !IsActive;

        if (IsActive && nextRunAt.HasValue)
        {
            NextRunAt = nextRunAt.Value;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkExecuted(DateTimeOffset nextRunAt)
    {
        LastRunAt = DateTimeOffset.UtcNow;
        NextRunAt = nextRunAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// INV-JOB-005: Schedule name must not be empty.
    /// </summary>
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("INV-JOB-005: Schedule name must not be empty.", nameof(name));
        }
    }

    /// <summary>
    /// INV-JOB-004: cron expression must not be empty (format validation is done in infrastructure).
    /// </summary>
    private static void ValidateCronExpressionNotEmpty(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new ArgumentException(
                "INV-JOB-004: Cron expression must not be empty.", nameof(cronExpression));
        }
    }
}
