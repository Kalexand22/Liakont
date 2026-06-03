namespace Stratum.Common.Infrastructure.GridPreferences;

using System.Data;
using System.Text.Json;
using Dapper;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Dapper-backed implementation of <see cref="ISavedFilterService"/>
/// using the <c>grid.saved_filters</c> table.
/// </summary>
public sealed class PostgresSavedFilterService : ISavedFilterService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionFactory _connectionFactory;

    public PostgresSavedFilterService(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<SavedFilter>> ListAsync(
        Guid userId, string gridKey, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id           AS Id,
                   user_id      AS UserId,
                   grid_key     AS GridKey,
                   name         AS Name,
                   filter_group AS FilterGroupJson,
                   is_default   AS IsDefault,
                   shared_with  AS SharedWith,
                   created_at   AS CreatedAt,
                   updated_at   AS UpdatedAt,
                   source       AS Source
            FROM grid.saved_filters
            WHERE (user_id = @UserId OR shared_with = @SharedWithEveryone)
              AND grid_key = @GridKey
            ORDER BY name
            """;

        var rows = await conn.QueryAsync<SavedFilterRow>(
            new CommandDefinition(
                sql,
                new { UserId = userId, GridKey = gridKey, SharedWithEveryone = (short)SharedScope.Everyone },
                cancellationToken: ct));

        return rows.Select(MapToModel).ToList().AsReadOnly();
    }

    public async Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id           AS Id,
                   user_id      AS UserId,
                   grid_key     AS GridKey,
                   name         AS Name,
                   filter_group AS FilterGroupJson,
                   is_default   AS IsDefault,
                   shared_with  AS SharedWith,
                   created_at   AS CreatedAt,
                   updated_at   AS UpdatedAt,
                   source       AS Source
            FROM grid.saved_filters
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync<SavedFilterRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        return row is null ? null : MapToModel(row);
    }

    public async Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        var filterGroupJson = JsonSerializer.Serialize(filter.FilterGroup, JsonOptions);
        var id = filter.Id == Guid.Empty ? Guid.NewGuid() : filter.Id;

        const string sql = """
            INSERT INTO grid.saved_filters
                (id, user_id, grid_key, name, filter_group, is_default, shared_with, source, created_at)
            VALUES
                (@Id, @UserId, @GridKey, @Name, @FilterGroup::jsonb, @IsDefault, @SharedWith, @Source, now())
            ON CONFLICT (id) DO UPDATE
                SET name         = @Name,
                    filter_group = @FilterGroup::jsonb,
                    is_default   = @IsDefault,
                    shared_with  = @SharedWith,
                    source       = @Source,
                    updated_at   = now()
            RETURNING id           AS Id,
                      user_id      AS UserId,
                      grid_key     AS GridKey,
                      name         AS Name,
                      filter_group AS FilterGroupJson,
                      is_default   AS IsDefault,
                      shared_with  AS SharedWith,
                      created_at   AS CreatedAt,
                      updated_at   AS UpdatedAt,
                      source       AS Source
            """;

        var parameters = new
        {
            Id = id,
            UserId = filter.UserId,
            GridKey = filter.GridKey,
            Name = filter.Name,
            FilterGroup = filterGroupJson,
            IsDefault = filter.IsDefault,
            SharedWith = (short)filter.SharedWith,
            Source = (short)filter.Source,
        };

        var row = await conn.QuerySingleAsync<SavedFilterRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return MapToModel(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = "DELETE FROM grid.saved_filters WHERE id = @Id";

        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        // Clear existing default for the same user/grid, then set the new one.
        // Uses a CTE to find the target filter's user_id and grid_key.
        const string sql = """
            WITH target AS (
                SELECT user_id, grid_key FROM grid.saved_filters WHERE id = @Id
            )
            UPDATE grid.saved_filters sf
            SET is_default = (sf.id = @Id),
                updated_at = now()
            FROM target
            WHERE sf.user_id = target.user_id
              AND sf.grid_key = target.grid_key
              AND (sf.is_default = true OR sf.id = @Id)
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    private static SavedFilter MapToModel(SavedFilterRow row)
    {
        var filterGroup = JsonSerializer.Deserialize<FilterGroup>(row.FilterGroupJson, JsonOptions)
                          ?? new FilterGroup(FilterLogic.And, []);

        return new SavedFilter(
            Id: row.Id,
            UserId: row.UserId,
            GridKey: row.GridKey,
            Name: row.Name,
            FilterGroup: filterGroup,
            IsDefault: row.IsDefault,
            SharedWith: (SharedScope)row.SharedWith,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt,
            Source: (SavedFilterSource)row.Source);
    }

    /// <summary>Dapper row mapping target.</summary>
    private sealed class SavedFilterRow
    {
        public Guid Id { get; init; }

        public Guid UserId { get; init; }

        public string GridKey { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string FilterGroupJson { get; init; } = "{}";

        public bool IsDefault { get; init; }

        public short SharedWith { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public short Source { get; init; } = (short)SavedFilterSource.Advanced;
    }
}
