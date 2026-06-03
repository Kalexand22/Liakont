namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record UserActionEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    public required UserActionType ActionType { get; init; }

    public required string Description { get; init; }

    public string? Target { get; init; }

    public string? Value { get; init; }

    public string? Url { get; init; }
}
