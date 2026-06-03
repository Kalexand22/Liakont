namespace Stratum.Modules.Identity.Infrastructure.Services;

using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Application.Preferences;

internal sealed class PostgresUserPreferencesService : IUserPreferencesService
{
    private static readonly HashSet<string> AllowedThemes = new(StringComparer.Ordinal)
    {
        UserPreferences.ThemeLight,
        UserPreferences.ThemeDark,
        UserPreferences.ThemeSystem,
    };

    private static readonly HashSet<string> AllowedDensities = new(StringComparer.Ordinal)
    {
        UserPreferences.DensityCompact,
        UserPreferences.DensityStandard,
    };

    private readonly IConnectionFactory _connectionFactory;

    public PostgresUserPreferencesService(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT theme      AS Theme,
                   language   AS Language,
                   density    AS Density,
                   extensions AS ExtensionsJson,
                   updated_at AS UpdatedAt
            FROM identity.user_preferences
            WHERE user_id = @UserId
            """;

        var row = await conn.QuerySingleOrDefaultAsync<PreferencesRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));

        return row is null ? null : MapToModel(row);
    }

    public async Task<UserPreferences> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        return existing ?? UserPreferences.Default;
    }

    public async Task UpdateAsync(Guid userId, UserPreferences preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Validate(preferences);

        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            INSERT INTO identity.user_preferences
                (user_id, theme, language, density, extensions, updated_at)
            VALUES
                (@UserId, @Theme, @Language, @Density, @Extensions::jsonb, @UpdatedAt)
            ON CONFLICT (user_id) DO UPDATE
                SET theme      = EXCLUDED.theme,
                    language   = EXCLUDED.language,
                    density    = EXCLUDED.density,
                    extensions = EXCLUDED.extensions,
                    updated_at = EXCLUDED.updated_at
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                UserId = userId,
                preferences.Theme,
                preferences.Language,
                preferences.Density,
                Extensions = preferences.ExtensionsJson,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken: ct));
    }

    private static void Validate(UserPreferences preferences)
    {
        if (!AllowedThemes.Contains(preferences.Theme))
        {
            throw new ArgumentException(
                $"Invalid theme '{preferences.Theme}'. Allowed: light, dark, system.",
                nameof(preferences));
        }

        if (!AllowedDensities.Contains(preferences.Density))
        {
            throw new ArgumentException(
                $"Invalid density '{preferences.Density}'. Allowed: compact, standard.",
                nameof(preferences));
        }

        if (string.IsNullOrWhiteSpace(preferences.Language))
        {
            throw new ArgumentException(
                "Language must be a non-empty culture code.",
                nameof(preferences));
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(preferences.Language);
        }
        catch (CultureNotFoundException ex)
        {
            throw new ArgumentException(
                $"Invalid language '{preferences.Language}'. Expected a valid culture code (e.g. 'fr-FR').",
                nameof(preferences),
                ex);
        }

        // On Linux/ICU, GetCultureInfo is permissive and synthesizes a "custom" culture
        // (LCID 0x1000) for unknown codes like "not-a-culture". Reject those explicitly.
        if (culture.LCID == 0x1000)
        {
            throw new ArgumentException(
                $"Invalid language '{preferences.Language}'. Expected a valid culture code (e.g. 'fr-FR').",
                nameof(preferences));
        }

        if (string.IsNullOrWhiteSpace(preferences.ExtensionsJson))
        {
            throw new ArgumentException(
                "ExtensionsJson must be a valid JSON object (use '{}' for empty).",
                nameof(preferences));
        }

        if (Encoding.UTF8.GetByteCount(preferences.ExtensionsJson) > UserPreferences.MaxExtensionsJsonBytes)
        {
            throw new ArgumentException(
                $"ExtensionsJson exceeds the {UserPreferences.MaxExtensionsJsonBytes}-byte limit.",
                nameof(preferences));
        }

        try
        {
            using var doc = JsonDocument.Parse(preferences.ExtensionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "ExtensionsJson must be a JSON object at the root (e.g. '{}').",
                    nameof(preferences));
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                "ExtensionsJson is not valid JSON.",
                nameof(preferences),
                ex);
        }
    }

    private static UserPreferences MapToModel(PreferencesRow row) => new()
    {
        Theme = row.Theme,
        Language = row.Language,
        Density = row.Density,
        ExtensionsJson = row.ExtensionsJson,
        UpdatedAt = row.UpdatedAt,
    };

    private sealed class PreferencesRow
    {
        public string Theme { get; init; } = UserPreferences.ThemeSystem;

        public string Language { get; init; } = UserPreferences.DefaultLanguage;

        public string Density { get; init; } = UserPreferences.DensityStandard;

        public string ExtensionsJson { get; init; } = UserPreferences.DefaultExtensionsJson;

        public DateTimeOffset? UpdatedAt { get; init; }
    }
}
