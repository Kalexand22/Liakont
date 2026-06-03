namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record CaptureBundle
{
    public required Guid Id { get; init; }

    public required CaptureType Type { get; init; }

    public string Title { get; init; } = string.Empty;

    public required CaptureMetadata Metadata { get; init; }

    public required IReadOnlyList<MediaCapture> Medias { get; init; }

    public required IReadOnlyList<LogEntry> Logs { get; init; }

    public required IReadOnlyList<HttpTrafficRecord> HttpTraffic { get; init; }

    public required IReadOnlyList<UserActionEntry> UserActions { get; init; }

    public required IReadOnlyList<string> Comments { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public VideoAnalysis? VideoAnalysis { get; init; }

    public IReadOnlyList<BrowserConsoleEntry> BrowserConsoleLogs { get; init; } = [];
}
