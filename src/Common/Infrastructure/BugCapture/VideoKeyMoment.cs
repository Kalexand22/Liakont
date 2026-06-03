namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record VideoKeyMoment
{
    public double TimestampSeconds { get; init; }

    public string Description { get; init; } = string.Empty;
}
