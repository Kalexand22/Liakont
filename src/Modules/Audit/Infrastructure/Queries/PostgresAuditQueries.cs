namespace Stratum.Modules.Audit.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

public sealed class PostgresAuditQueries : IAuditQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresAuditQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<FieldChangeDto>> GetFieldChanges(
        string entityType,
        string entityId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT id, entry_id, entity_type, entity_id, field_name,
                   old_value::text, new_value::text, actor_id, occurred_at
            FROM audit.field_changes
            WHERE entity_type = @EntityType AND entity_id = @EntityId
            ORDER BY occurred_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(
                sql,
                new { EntityType = entityType, EntityId = entityId, PageSize = pageSize, Offset = offset },
                cancellationToken: cancellationToken));

        return rows.Select(MapFieldChange).ToList();
    }

    public async Task<IReadOnlyList<AuditPolicyDto>> GetAuditPolicies(
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, entity_type, module_source, is_enabled, tracked_fields, created_at, updated_at
            FROM audit_module.audit_policies
            ORDER BY entity_type
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return rows.Select(MapPolicy).ToList();
    }

    public async Task<AuditPolicyDto?> GetPolicyByEntityType(
        string entityType,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, entity_type, module_source, is_enabled, tracked_fields, created_at, updated_at
            FROM audit_module.audit_policies
            WHERE entity_type = @EntityType
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { EntityType = entityType }, cancellationToken: cancellationToken));

        return row is null ? null : MapPolicy(row);
    }

    public async Task<AuditPolicyDto?> GetPolicyById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, entity_type, module_source, is_enabled, tracked_fields, created_at, updated_at
            FROM audit_module.audit_policies
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : MapPolicy(row);
    }

    public async Task<IReadOnlyList<ActivityDto>> GetActivities(
        string entityType,
        string entityId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT id, entity_type, entity_id, activity_type, description,
                   actor_id, metadata::text, company_id, created_at
            FROM audit.activities
            WHERE entity_type = @EntityType AND entity_id = @EntityId
            ORDER BY created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(
                sql,
                new { EntityType = entityType, EntityId = entityId, PageSize = pageSize, Offset = offset },
                cancellationToken: cancellationToken));

        return rows.Select(MapActivity).ToList();
    }

    public async Task<ActivityDto?> GetActivityById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, entity_type, entity_id, activity_type, description,
                   actor_id, metadata::text, company_id, created_at
            FROM audit.activities
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : MapActivity(row);
    }

    public async Task<IReadOnlyList<AuditSearchResultDto>> SearchEntries(
        string? actorId = null,
        string? entityType = null,
        string? activityType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            conditions.Add("a.actor_id = @ActorId");
            parameters.Add("ActorId", actorId);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            conditions.Add("a.entity_type = @EntityType");
            parameters.Add("EntityType", entityType);
        }

        if (!string.IsNullOrWhiteSpace(activityType))
        {
            conditions.Add("a.activity_type = @ActivityType");
            parameters.Add("ActivityType", activityType);
        }

        if (dateFrom.HasValue)
        {
            conditions.Add("a.created_at >= @From");
            parameters.Add("From", dateFrom.Value.UtcDateTime);
        }

        if (dateTo.HasValue)
        {
            conditions.Add("a.created_at <= @To");
            parameters.Add("To", dateTo.Value.UtcDateTime);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            conditions.Add("(a.description ILIKE @Search OR a.metadata::text ILIKE @Search)");
            parameters.Add("Search", $"%{searchText}%");
        }

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        var sql = $"""
            SELECT a.id, a.entity_type, a.entity_id, a.activity_type, a.description,
                   a.actor_id, a.metadata::text, a.company_id, a.created_at,
                   fc.change_count
            FROM audit.activities a
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::int AS change_count
                FROM audit.field_changes fc
                WHERE fc.entity_type = a.entity_type AND fc.entity_id = a.entity_id
                  AND fc.occurred_at BETWEEN a.created_at - INTERVAL '2 seconds' AND a.created_at + INTERVAL '2 seconds'
            ) fc ON TRUE
            {whereClause}
            ORDER BY a.created_at DESC
            LIMIT 500
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return rows.Select(MapSearchResult).ToList();
    }

    public async Task<IReadOnlyList<FieldChangeDto>> GetFieldChangesByEntryId(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, entry_id, entity_type, entity_id, field_name,
                   old_value::text, new_value::text, actor_id, occurred_at
            FROM audit.field_changes
            WHERE entry_id = @EntryId
            ORDER BY occurred_at ASC
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { EntryId = entryId }, cancellationToken: cancellationToken));

        return rows.Select(MapFieldChange).ToList();
    }

    public async Task<IReadOnlyList<FieldChangeDto>> GetCorrelatedFieldChanges(
        string entityType,
        string entityId,
        DateTimeOffset activityTime,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, entry_id, entity_type, entity_id, field_name,
                   old_value::text, new_value::text, actor_id, occurred_at
            FROM audit.field_changes
            WHERE entity_type = @EntityType AND entity_id = @EntityId
              AND occurred_at BETWEEN @TimeFrom AND @TimeTo
            ORDER BY occurred_at ASC
            """;

        var parameters = new
        {
            EntityType = entityType,
            EntityId = entityId,
            TimeFrom = activityTime.AddSeconds(-2).UtcDateTime,
            TimeTo = activityTime.AddSeconds(2).UtcDateTime,
        };

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return rows.Select(MapFieldChange).ToList();
    }

    private static AuditSearchResultDto MapSearchResult(dynamic row) => new()
    {
        Id = (Guid)row.id,
        EntityType = (string)row.entity_type,
        EntityId = (string)row.entity_id,
        ActivityType = (string)row.activity_type,
        Description = (string)row.description,
        ActorId = (string)row.actor_id,
        Metadata = (string?)row.metadata,
        CompanyId = (Guid?)row.company_id,
        CreatedAt = new DateTimeOffset((DateTime)row.created_at, TimeSpan.Zero),
        ChangeCount = (int)row.change_count,
    };

    private static FieldChangeDto MapFieldChange(dynamic row) => new()
    {
        Id = (Guid)row.id,
        EntryId = (Guid)row.entry_id,
        EntityType = (string)row.entity_type,
        EntityId = (string)row.entity_id,
        FieldName = (string)row.field_name,
        OldValue = (string?)row.old_value,
        NewValue = (string?)row.new_value,
        ActorId = (string)row.actor_id,
        OccurredAt = new DateTimeOffset((DateTime)row.occurred_at, TimeSpan.Zero),
    };

    private static ActivityDto MapActivity(dynamic row) => new()
    {
        Id = (Guid)row.id,
        EntityType = (string)row.entity_type,
        EntityId = (string)row.entity_id,
        ActivityType = (string)row.activity_type,
        Description = (string)row.description,
        ActorId = (string)row.actor_id,
        Metadata = (string?)row.metadata,
        CompanyId = (Guid?)row.company_id,
        CreatedAt = new DateTimeOffset((DateTime)row.created_at, TimeSpan.Zero),
    };

    private static AuditPolicyDto MapPolicy(dynamic row) => new()
    {
        Id = (Guid)row.id,
        EntityType = (string)row.entity_type,
        ModuleSource = (string)row.module_source,
        IsEnabled = (bool)row.is_enabled,
        TrackedFields = row.tracked_fields is string[] arr ? arr : [],
        CreatedAt = new DateTimeOffset((DateTime)row.created_at, TimeSpan.Zero),
        UpdatedAt = row.updated_at is null
            ? (DateTimeOffset?)null
            : new DateTimeOffset((DateTime)row.updated_at, TimeSpan.Zero),
    };
}
