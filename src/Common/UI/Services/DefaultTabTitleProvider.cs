namespace Stratum.Common.UI.Services;

/// <summary>
/// Fallback title provider that humanizes the last URL segment.
/// Host should register a localized implementation to override this.
/// </summary>
internal sealed class DefaultTabTitleProvider : ITabTitleProvider
{
    public string GetTitle(string url)
    {
        var path = url.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "Home";
        }

        var lastSegment = path.Split('/')[^1];

        // Skip GUID-like segments — use the parent segment instead
        if (Guid.TryParse(lastSegment, out _))
        {
            var segments = path.Split('/');
            lastSegment = segments.Length >= 2 ? segments[^2] : lastSegment;
        }

        // Humanize: replace hyphens with spaces and capitalize first letter
        var humanized = lastSegment.Replace('-', ' ');
        if (humanized.Length > 0)
        {
            humanized = char.ToUpperInvariant(humanized[0]) + humanized[1..];
        }

        return humanized;
    }
}
