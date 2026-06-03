namespace Stratum.Common.Infrastructure.CrossTenant;

/// <summary>
/// Configuration options for the cross-tenant event dispatcher.
/// </summary>
public sealed class CrossTenantDispatcherOptions
{
    public const string SectionName = "CrossTenantDispatcher";

    /// <summary>
    /// Interval between polling cycles when no events are pending.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of events to fetch per polling cycle.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Number of delivery failures after which an event is marked as dead-letter.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
}
