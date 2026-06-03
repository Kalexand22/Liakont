namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record CaptureConfiguration
{
    public bool Enabled { get; init; } = true;

    public int MaxLogs { get; init; } = 1000;

    public int MaxHttpRecords { get; init; } = 100;

    public int MaxUserActions { get; init; } = 500;

    public int MaxBodySizeBytes { get; init; } = 32768;

    public string ScreenshotFormat { get; init; } = "png";

    public string? ProjectName { get; init; }

    public string ReportsPath { get; init; } = "reports/";

    public string? WhisperApiKey { get; init; }

    public string WhisperModel { get; init; } = "whisper-1";

    public string? OpenRouterApiKey { get; init; }

    public string VideoAnalysisModel { get; init; } = "google/gemini-3-flash-preview";

    public GitHubIssueConfiguration? GitHub { get; init; }
}
