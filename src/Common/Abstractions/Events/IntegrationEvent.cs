namespace Stratum.Common.Abstractions.Events;

public record IntegrationEvent<TPayload>
{
    public required Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required Guid CorrelationId { get; init; }

    public required string ModuleSource { get; init; }

    public required TPayload Payload { get; init; }

    public required int Version { get; init; }
}
