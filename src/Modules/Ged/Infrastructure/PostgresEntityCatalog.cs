namespace Liakont.Modules.Ged.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Domain.Catalog;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lecture Dapper du catalogue de TYPES d'entité GED (<c>ged_catalog.entity_types</c>), résolue sur la base DU TENANT
/// (isolation = la connexion, F19 §3.2/§3.8), symétrique de <see cref="PostgresAxisCatalog"/>. Les colonnes
/// <c>snake_case</c> sont ALIASÉES vers les propriétés (Dapper n'active pas <c>MatchNamesWithUnderscores</c> dans ce
/// dépôt — sans alias la propriété resterait silencieusement vide).
/// </summary>
internal sealed class PostgresEntityCatalog : IEntityCatalog
{
    private const string ResolveSql = """
        SELECT id              AS Id,
               code            AS Code,
               identity_key    AS IdentityKey,
               is_confidential AS IsConfidential,
               is_active       AS IsActive
        FROM ged_catalog.entity_types
        WHERE code = @Code
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresEntityCatalog(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<EntityTypeDefinition?> ResolveAsync(string entityTypeCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityTypeCode);

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<EntityTypeRow>(new CommandDefinition(
            ResolveSql,
            new { Code = entityTypeCode },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new EntityTypeDefinition
        {
            Id = row.Id,
            Code = row.Code,
            IdentityKey = row.IdentityKey,
            IsConfidential = row.IsConfidential,
            IsActive = row.IsActive,
        };
    }

    private sealed class EntityTypeRow
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string? IdentityKey { get; set; }

        public bool IsConfidential { get; set; }

        public bool IsActive { get; set; }
    }
}
