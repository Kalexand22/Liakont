namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record BrowserConsoleEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    public required string Level { get; init; }

    public required string Message { get; init; }
}
