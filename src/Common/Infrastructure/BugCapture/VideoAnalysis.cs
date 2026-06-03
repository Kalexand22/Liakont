namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record VideoAnalysis
{
    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string StepsToReproduce { get; init; } = string.Empty;

    public IReadOnlyList<VideoKeyMoment> KeyMoments { get; init; } = [];
}
