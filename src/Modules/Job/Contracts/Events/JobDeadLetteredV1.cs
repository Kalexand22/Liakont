namespace Stratum.Modules.Job.Contracts.Events;

public record JobDeadLetteredV1
{
    public required Guid JobId { get; init; }

    public required string Type { get; init; }

    public required string ErrorMessage { get; init; }

    public required int MaxRetries { get; init; }
}
