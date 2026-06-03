namespace Stratum.Modules.Job.Contracts.DTOs;

public record JobDto
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Status { get; init; }

    public required int Priority { get; init; }

    public required int MaxRetries { get; init; }

    public required int RetryCount { get; init; }

    public required DateTimeOffset ScheduledAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? ErrorMessage { get; init; }

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
