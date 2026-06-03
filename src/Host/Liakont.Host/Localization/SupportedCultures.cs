namespace Liakont.Host.Localization;

using System.Globalization;

internal static class SupportedCultures
{
    public static readonly string DefaultCulture = "en";

    private static readonly CultureInfo[] Cultures =
    [
        new("en"),
        new("fr"),
    ];

    /// <summary>
    /// Gets the supported cultures. Returns a new array each time to prevent mutation.
    /// </summary>
    public static CultureInfo[] All => (CultureInfo[])Cultures.Clone();

    public static bool IsSupported(string cultureName)
    {
        return Array.Exists(Cultures, c => string.Equals(c.Name, cultureName, StringComparison.OrdinalIgnoreCase));
    }
}
