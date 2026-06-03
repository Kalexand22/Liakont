namespace Liakont.Host.Security;

/// <summary>
/// Validates and sanitizes return URLs to prevent open-redirect attacks.
/// Only relative URLs starting with "/" are accepted.
/// </summary>
internal static class ReturnUrlSanitizer
{
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.Contains('\\'))
        {
            return returnUrl;
        }

        return "/";
    }
}
