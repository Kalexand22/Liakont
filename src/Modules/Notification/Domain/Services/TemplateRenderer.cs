namespace Stratum.Modules.Notification.Domain.Services;

using System.Text.RegularExpressions;

public static partial class TemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return placeholders.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();
}
