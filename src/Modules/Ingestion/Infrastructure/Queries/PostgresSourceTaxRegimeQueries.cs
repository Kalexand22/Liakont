namespace Liakont.Modules.Ingestion.Infrastructure.Queries;

using Dapper;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures des régimes de TVA source observés (base système, schéma <c>ingestion</c>), scopées par
/// <c>tenant_id</c>. Jamais de lecture cross-tenant. Consommé par TVA03 (détection de couverture).
/// </summary>
internal sealed class PostgresSourceTaxRegimeQueries : ISourceTaxRegimeQueries
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresSourceTaxRegimeQueries(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<IReadOnlyList<SourceTaxRegimeSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT code, label, occurrences, last_seen_at
            FROM ingestion.source_tax_regimes
            WHERE tenant_id = @TenantId
            ORDER BY code ASC
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));

        var result = new List<SourceTaxRegimeSummaryDto>();
        foreach (var row in rows)
        {
            result.Add(new SourceTaxRegimeSummaryDto
            {
                Code = (string)row.code,
                Label = (string?)row.label,
                Occurrences = (long)row.occurrences,
                LastSeenAtUtc = IngestionRowReader.ToDateTimeOffset((object)row.last_seen_at),
            });
        }

        return result;
    }
}
