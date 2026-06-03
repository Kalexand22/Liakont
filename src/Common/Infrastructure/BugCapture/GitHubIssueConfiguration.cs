namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record GitHubIssueConfiguration
{
    public string Owner { get; init; } = string.Empty;

    public string Repo { get; init; } = string.Empty;

    public string? Token { get; init; }

    public IReadOnlyList<string> DefaultLabels { get; init; } = ["bug-capture"];
}
