namespace Stratum.Common.UI.Helpers;

using System.Text.RegularExpressions;

/// <summary>
/// Lightweight whitelist-based HTML sanitizer for RichTextEditor output.
/// Strips all tags/attributes not in the whitelist to prevent XSS.
/// </summary>
internal static partial class HtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s", "strike",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li",
        "blockquote", "pre", "code",
        "a", "img",
        "span", "sub", "sup",
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedAttributesByTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = new(StringComparer.OrdinalIgnoreCase) { "href", "target", "rel" },
        ["img"] = new(StringComparer.OrdinalIgnoreCase) { "src", "alt", "width", "height" },
        ["span"] = new(StringComparer.OrdinalIgnoreCase) { "class", "style" },
        ["p"] = new(StringComparer.OrdinalIgnoreCase) { "class", "style" },
        ["pre"] = new(StringComparer.OrdinalIgnoreCase) { "class" },
        ["code"] = new(StringComparer.OrdinalIgnoreCase) { "class" },
    };

    private static readonly HashSet<string> AllowedCssProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "background-color", "text-align", "text-decoration",
        "font-weight", "font-style",
    };

    /// <summary>
    /// Sanitizes HTML by removing disallowed tags and attributes.
    /// </summary>
    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Remove script/style/object/embed tags entirely (content included)
        html = DangerousTagRegex().Replace(html, string.Empty);

        // Remove event handler attributes (onclick, onerror, etc.)
        html = EventHandlerRegex().Replace(html, "$1");

        // Process each HTML tag
        html = TagRegex().Replace(html, match =>
        {
            var isClosing = match.Groups[1].Value == "/";
            var tagName = match.Groups[2].Value;

            if (!AllowedTags.Contains(tagName))
            {
                return string.Empty;
            }

            if (isClosing)
            {
                return $"</{tagName}>";
            }

            var attributes = match.Groups[3].Value;
            var sanitizedAttrs = SanitizeAttributes(tagName, attributes);

            return string.IsNullOrEmpty(sanitizedAttrs)
                ? $"<{tagName}>"
                : $"<{tagName} {sanitizedAttrs}>";
        });

        return html;
    }

    private static string SanitizeAttributes(string tagName, string attributeString)
    {
        if (string.IsNullOrWhiteSpace(attributeString))
        {
            return string.Empty;
        }

        if (!AllowedAttributesByTag.TryGetValue(tagName, out var allowedAttrs))
        {
            return string.Empty;
        }

        var result = new List<string>();

        foreach (Match attr in AttributeRegex().Matches(attributeString))
        {
            var name = attr.Groups[1].Value;
            var value = attr.Groups[2].Value;

            if (!allowedAttrs.Contains(name))
            {
                continue;
            }

            // Sanitize href/src: block javascript: protocol
            if (name is "href" or "src")
            {
                var trimmed = value.Trim();
                if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            // Sanitize style attribute
            if (name == "style")
            {
                value = SanitizeStyle(value);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }
            }

            var encoded = value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
            result.Add($"{name}=\"{encoded}\"");
        }

        return string.Join(" ", result);
    }

    private static string SanitizeStyle(string style)
    {
        var allowed = new List<string>();

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = declaration.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var property = parts[0].Trim();
            var value = parts[1].Trim();

            if (AllowedCssProperties.Contains(property)
                && !value.Contains("url(", StringComparison.OrdinalIgnoreCase)
                && !value.Contains("expression(", StringComparison.OrdinalIgnoreCase))
            {
                allowed.Add($"{property}: {value}");
            }
        }

        return string.Join("; ", allowed);
    }

    [GeneratedRegex(@"<\s*(script|style|object|embed|applet|iframe|form)[^>]*>.*?</\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousTagRegex();

    [GeneratedRegex(@"(<[^>]*?)\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerRegex();

    [GeneratedRegex(@"<\s*(/?)(\w+)([^>]*)>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(\w[\w-]*)\s*=\s*""([^""]*)""")]
    private static partial Regex AttributeRegex();
}
