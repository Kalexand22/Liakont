namespace Liakont.Host.Services;

using Microsoft.Extensions.Localization;
using Stratum.Common.UI.Services;

/// <summary>
/// Localized tab title provider that maps URL segments to resource keys
/// from <see cref="HostResources"/>. Falls back to humanizing the segment
/// if no resource key is found.
/// </summary>
internal sealed class LocalizedTabTitleProvider(IStringLocalizer<HostResources> localizer) : ITabTitleProvider
{
    /// <summary>
    /// Maps URL segments to resource keys in HostResources.
    /// Mirrors the breadcrumb mapping in ErpShellLayout.
    /// </summary>
    private static readonly Dictionary<string, string> SegmentResourceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        [string.Empty] = "Breadcrumb_Home",
        ["parties"] = "Breadcrumb_Parties",
        ["quotes"] = "Breadcrumb_Quotes",
        ["create"] = "Breadcrumb_Create",
        ["edit"] = "Breadcrumb_Edit",
        ["showcase"] = "Breadcrumb_Showcase",
        ["components"] = "Breadcrumb_Components",
        ["foundations"] = "Breadcrumb_Foundations",
        ["products"] = "Breadcrumb_Products",
        ["orders"] = "Breadcrumb_Orders",
        ["settings"] = "Breadcrumb_Settings",
        ["preferences"] = "Breadcrumb_Preferences",

        // Notification module
        ["routing"] = "Tab_RoutingRules",
    };

    public string GetTitle(string url)
    {
        var path = url.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return localizer["Breadcrumb_Home"].Value;
        }

        var segments = path.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

        // Walk segments from the end, skipping GUIDs, to find the best title
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var segment = segments[i];

            // Skip GUID-like segments
            if (Guid.TryParse(segment, out _))
            {
                continue;
            }

            if (SegmentResourceKeys.TryGetValue(segment, out var key))
            {
                return localizer[key].Value;
            }

            // Humanize: replace hyphens with spaces and capitalize
            var humanized = segment.Replace('-', ' ');
            if (humanized.Length > 0)
            {
                humanized = char.ToUpperInvariant(humanized[0]) + humanized[1..];
            }

            return humanized;
        }

        // All segments were GUIDs — use first non-empty parent
        return localizer["Breadcrumb_Home"].Value;
    }
}
