namespace Stratum.Modules.Audit.Infrastructure.Repositories;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Audit.Domain.Entities;
using Stratum.Modules.Audit.Domain.Repositories;

public sealed class PostgresAuditPolicyRepository : IAuditPolicyRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresAuditPolicyRepository(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AuditPolicy?> GetByEntityType(string entityType, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, entity_type, module_source, is_enabled, tracked_fields, created_at, updated_at
            FROM audit_module.audit_policies
            WHERE entity_type = @EntityType
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { EntityType = entityType }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        DateTimeOffset? updatedAt = row.updated_at is null
            ? null
            : new DateTimeOffset((DateTime)row.updated_at, TimeSpan.Zero);

        return AuditPolicy.Reconstitute(
            (Guid)row.id,
            (string)row.entity_type,
            (string)row.module_source,
            (bool)row.is_enabled,
            row.tracked_fields is string[] arr ? arr : [],
            new DateTimeOffset((DateTime)row.created_at, TimeSpan.Zero),
            updatedAt);
    }

    public async Task Insert(AuditPolicy policy, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            INSERT INTO audit_module.audit_policies (id, entity_type, module_source, is_enabled, tracked_fields, created_at, updated_at)
            VALUES (@Id, @EntityType, @ModuleSource, @IsEnabled, @TrackedFields, @CreatedAt, @UpdatedAt)
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    policy.Id,
                    policy.EntityType,
                    policy.ModuleSource,
                    policy.IsEnabled,
                    TrackedFields = policy.TrackedFields.ToArray(),
                    policy.CreatedAt,
                    policy.UpdatedAt,
                },
                cancellationToken: ct));
    }

    public async Task Update(AuditPolicy policy, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            UPDATE audit_module.audit_policies
            SET module_source = @ModuleSource,
                is_enabled = @IsEnabled,
                tracked_fields = @TrackedFields,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    policy.Id,
                    policy.ModuleSource,
                    policy.IsEnabled,
                    TrackedFields = policy.TrackedFields.ToArray(),
                    policy.UpdatedAt,
                },
                cancellationToken: ct));
    }
}
