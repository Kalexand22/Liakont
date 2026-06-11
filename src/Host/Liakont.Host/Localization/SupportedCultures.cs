namespace Liakont.Host.Localization;

using System.Globalization;

internal static class SupportedCultures
{
    // « fr » par défaut : Liakont est un produit de conformité fiscale FRANÇAIS (messages opérateur
    // en français, CLAUDE.md n°12) — décision opérateur 2026-06-10 (bug-inbox console-web).
    public static readonly string DefaultCulture = "fr";

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
