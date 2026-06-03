namespace Stratum.Modules.Identity.Application.Preferences;

public sealed record UserPreferences
{
    public const string ThemeLight = "light";

    public const string ThemeDark = "dark";

    public const string ThemeSystem = "system";

    public const string DensityCompact = "compact";

    public const string DensityStandard = "standard";

    public const string DefaultLanguage = "fr-FR";

    public const string DefaultExtensionsJson = "{}";

    public const int MaxExtensionsJsonBytes = 4096;

    public string Theme { get; init; } = ThemeSystem;

    public string Language { get; init; } = DefaultLanguage;

    public string Density { get; init; } = DensityStandard;

    public string ExtensionsJson { get; init; } = DefaultExtensionsJson;

    public DateTimeOffset? UpdatedAt { get; init; }

    public static UserPreferences Default { get; } = new();
}
