namespace Stratum.Common.Infrastructure.GridPreferences;

using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Dapper-backed implementation of <see cref="IGridPreferenceService"/>
/// using the <c>grid.user_preferences</c> table.
/// </summary>
public sealed partial class PostgresGridPreferenceService : IGridPreferenceService
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresGridPreferenceService> _logger;

    public PostgresGridPreferenceService(
        IConnectionFactory connectionFactory,
        ILogger<PostgresGridPreferenceService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<UserGridPreference?> GetPreferenceAsync(
        Guid userId, string gridKey, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id             AS Id,
                   user_id        AS UserId,
                   grid_key       AS GridKey,
                   column_keys    AS ColumnKeysJson,
                   preferred_view AS PreferredView,
                   filter_state   AS FilterStateJson,
                   column_widths  AS ColumnWidthsJson,
                   created_at     AS CreatedAt,
                   updated_at     AS UpdatedAt
            FROM grid.user_preferences
            WHERE user_id = @UserId AND grid_key = @GridKey
            """;

        var row = await conn.QuerySingleOrDefaultAsync<PreferenceRow>(
            new CommandDefinition(sql, new { UserId = userId, GridKey = gridKey }, cancellationToken: ct));

        return row is null ? null : MapToModel(row, _logger);
    }

    public async Task SavePreferenceAsync(
        Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        var json = JsonSerializer.Serialize(columnKeys);

        const string sql = """
            INSERT INTO grid.user_preferences (id, user_id, grid_key, column_keys, created_at)
            VALUES (gen_random_uuid(), @UserId, @GridKey, @ColumnKeys::jsonb, now())
            ON CONFLICT (user_id, grid_key) DO UPDATE
                SET column_keys = @ColumnKeys::jsonb,
                    updated_at  = now()
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { UserId = userId, GridKey = gridKey, ColumnKeys = json }, cancellationToken: ct));
    }

    public async Task SaveViewPreferenceAsync(
        Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        var viewName = viewKind.ToString();

        const string sql = """
            INSERT INTO grid.user_preferences (id, user_id, grid_key, column_keys, preferred_view, created_at)
            VALUES (gen_random_uuid(), @UserId, @GridKey, '[]'::jsonb, @PreferredView, now())
            ON CONFLICT (user_id, grid_key) DO UPDATE
                SET preferred_view = @PreferredView,
                    updated_at     = now()
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { UserId = userId, GridKey = gridKey, PreferredView = viewName }, cancellationToken: ct));
    }

    public async Task SaveFilterStateAsync(
        Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        // Null clears any previously stored state; the JSON is stored as-is in
        // a jsonb column so it remains queryable by future polish features.
        const string sql = """
            INSERT INTO grid.user_preferences (id, user_id, grid_key, column_keys, filter_state, created_at)
            VALUES (gen_random_uuid(), @UserId, @GridKey, '[]'::jsonb,
                    CASE WHEN @FilterState IS NULL THEN NULL ELSE @FilterState::jsonb END,
                    now())
            ON CONFLICT (user_id, grid_key) DO UPDATE
                SET filter_state = CASE WHEN @FilterState IS NULL THEN NULL ELSE @FilterState::jsonb END,
                    updated_at   = now()
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { UserId = userId, GridKey = gridKey, FilterState = filterStateJson },
                cancellationToken: ct));
    }

    public async Task SaveColumnWidthsAsync(
        Guid userId,
        string gridKey,
        IReadOnlyDictionary<string, string> columnWidths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(columnWidths);

        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        // Always serialize — an empty dictionary yields "{}" which is the documented
        // "cleared" marker for downstream readers (see IGridPreferenceService).
        var json = JsonSerializer.Serialize(columnWidths);

        const string sql = """
            INSERT INTO grid.user_preferences (id, user_id, grid_key, column_keys, column_widths, created_at)
            VALUES (gen_random_uuid(), @UserId, @GridKey, '[]'::jsonb, @ColumnWidths::jsonb, now())
            ON CONFLICT (user_id, grid_key) DO UPDATE
                SET column_widths = @ColumnWidths::jsonb,
                    updated_at    = now()
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { UserId = userId, GridKey = gridKey, ColumnWidths = json },
                cancellationToken: ct));
    }

    private static UserGridPreference MapToModel(PreferenceRow row, ILogger logger)
    {
        var columnKeys = JsonSerializer.Deserialize<List<string>>(row.ColumnKeysJson) ?? [];
        ViewKind? preferredView = Enum.TryParse<ViewKind>(row.PreferredView, ignoreCase: true, out var vk)
            ? vk
            : null;

        IReadOnlyDictionary<string, string>? columnWidths = null;
        if (!string.IsNullOrWhiteSpace(row.ColumnWidthsJson))
        {
            // Best-effort: a corrupt row shouldn't crash preference reads. Callers
            // simply treat the widths as "not persisted" and fall back to defaults.
            try
            {
                columnWidths = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ColumnWidthsJson);
            }
            catch (JsonException ex)
            {
                LogColumnWidthsDeserializeFailed(logger, ex, row.GridKey, row.UserId, row.Id);
                columnWidths = null;
            }
        }

        return new UserGridPreference(
            Id: row.Id,
            UserId: row.UserId,
            GridKey: row.GridKey,
            ColumnKeys: columnKeys.AsReadOnly(),
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt,
            PreferredViewKind: preferredView,
            FilterStateJson: row.FilterStateJson,
            ColumnWidths: columnWidths);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to deserialize column_widths JSON for grid {GridKey} user {UserId} (row Id {RowId}); widths treated as not persisted")]
    private static partial void LogColumnWidthsDeserializeFailed(
        ILogger logger,
        Exception exception,
        string gridKey,
        Guid userId,
        Guid rowId);

    /// <summary>Dapper row mapping target with PascalCase properties (mapped via SQL aliases).</summary>
    private sealed class PreferenceRow
    {
        public Guid Id { get; init; }

        public Guid UserId { get; init; }

        public string GridKey { get; init; } = string.Empty;

        public string ColumnKeysJson { get; init; } = "[]";

        public string? PreferredView { get; init; }

        public string? FilterStateJson { get; init; }

        public string? ColumnWidthsJson { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }
    }
}
