namespace Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Represents a row from the outbox.dead_letter_events table.
/// </summary>
public sealed record DeadLetterEvent
{
    public Guid Id { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public Guid CorrelationId { get; init; }

    public string ModuleSource { get; init; } = string.Empty;

    public int Version { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public int RetryCount { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset MovedAt { get; init; }
}
