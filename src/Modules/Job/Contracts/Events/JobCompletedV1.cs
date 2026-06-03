namespace Stratum.Modules.Job.Contracts.Events;

public record JobCompletedV1
{
    public required Guid JobId { get; init; }

    public required string Type { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}
