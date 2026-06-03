namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record LogEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    public required string Level { get; init; }

    public required string Category { get; init; }

    public required string Message { get; init; }

    public int? EventId { get; init; }

    public ExceptionInfo? Exception { get; init; }
}
