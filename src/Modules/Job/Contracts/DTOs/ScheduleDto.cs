namespace Stratum.Modules.Job.Contracts.DTOs;

public record ScheduleDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string CronExpression { get; init; }

    public required string JobType { get; init; }

    public required string PayloadTemplate { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset NextRunAt { get; init; }

    public DateTimeOffset? LastRunAt { get; init; }

    public required Guid CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
