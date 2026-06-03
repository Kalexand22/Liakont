namespace Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Configuration options for the outbox background worker.
/// </summary>
public sealed class OutboxWorkerOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Interval between polling cycles when no events are pending.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of events to process per polling cycle.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Number of consecutive dispatch failures after which an event is moved to dead-letter.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
}
