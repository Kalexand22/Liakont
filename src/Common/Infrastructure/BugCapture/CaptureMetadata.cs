namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record CaptureMetadata
{
    public required string Project { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public required string Os { get; init; }

    public required string User { get; init; }

    public required string ScreenResolution { get; init; }

    public required string DotNetVersion { get; init; }
}
