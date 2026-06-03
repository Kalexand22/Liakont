namespace Stratum.Common.Infrastructure.Portal;

using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Portal;
using Stratum.Common.Abstractions.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Reads public data across all tenant databases using fan-out queries.
/// Operates outside tenant context. Resilient: a failing tenant is skipped with a warning.
/// </summary>
public sealed partial class FanOutPortalQueryService : IPortalQueryService
{
    private const int MaxConcurrentTenantQueries = 8;
    private const int MaxRowsPerTenant = 1000;

    /// <summary>
    /// Combined query that checks <c>portal.enabled</c> feature flag in the tenant's config
    /// and returns public parties in a single round-trip.
    /// If <c>portal.enabled</c> is not set or false, returns zero rows.
    /// </summary>
    private const string QueryPublicPartiesSql = """
        SELECT p.id          AS "EntityId",
               p.legal_name  AS "Title",
               p.created_at  AS "Date",
               p.notes       AS "Description"
        FROM party.parties p
        WHERE p.is_public = true
          AND p.is_active = true
          AND EXISTS (
              SELECT 1 FROM config.settings s
              WHERE s.key = 'feature.portal.enabled'
                AND s.value_type = 'bool'
                AND s.value = 'true'
          )
        """;

    private readonly ITenantQueries _tenantQueries;
    private readonly ITenantConnectionFactory _tenantConnectionFactory;
    private readonly ILogger<FanOutPortalQueryService> _logger;

    public FanOutPortalQueryService(
        ITenantQueries tenantQueries,
        ITenantConnectionFactory tenantConnectionFactory,
        ILogger<FanOutPortalQueryService> logger)
    {
        _tenantQueries = tenantQueries;
        _tenantConnectionFactory = tenantConnectionFactory;
        _logger = logger;
    }

    public async Task<ListResult<PublicEventDto>> GetPublicEventsAsync(
        PortalFilter filter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var tenants = await _tenantQueries.ListAsync(ct);
        var activeTenants = tenants.Where(t => t.IsActive).ToList();

        if (filter.TenantId is not null)
        {
            activeTenants = activeTenants.Where(t => t.Id == filter.TenantId).ToList();
        }

        if (activeTenants.Count == 0)
        {
            return new ListResult<PublicEventDto> { Items = [], TotalCount = 0 };
        }

        var allResults = new List<PublicEventDto>();

        await Parallel.ForEachAsync(
            activeTenants,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentTenantQueries, CancellationToken = ct },
            async (tenant, innerCt) =>
            {
                var results = await QueryTenantSafeAsync(tenant, filter, innerCt);
                if (results.Count > 0)
                {
                    lock (allResults)
                    {
                        allResults.AddRange(results);
                    }
                }
            });

        // Sort descending by date, then paginate
        var sorted = allResults
            .OrderByDescending(e => e.Date)
            .ToList();

        var totalCount = sorted.Count;
        var skip = (page - 1) * pageSize;
        var items = sorted.Skip(skip).Take(pageSize).ToList();

        return new ListResult<PublicEventDto>
        {
            Items = items,
            TotalCount = totalCount,
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to query public events from tenant '{TenantId}', skipping")]
    private static partial void LogTenantQueryFailed(ILogger logger, string tenantId, Exception exception);

    private static (string Sql, DynamicParameters Parameters) BuildQuery(PortalFilter filter)
    {
        var sql = QueryPublicPartiesSql;
        var parameters = new DynamicParameters();

        if (filter.DateFrom.HasValue)
        {
            sql += "\n  AND p.created_at >= @DateFrom";
            parameters.Add("DateFrom", filter.DateFrom.Value.UtcDateTime);
        }

        if (filter.DateTo.HasValue)
        {
            sql += "\n  AND p.created_at <= @DateTo";
            parameters.Add("DateTo", filter.DateTo.Value.UtcDateTime);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var escaped = filter.Keyword
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
            sql += "\n  AND (p.legal_name ILIKE @Keyword ESCAPE '\\' OR p.trade_name ILIKE @Keyword ESCAPE '\\' OR p.notes ILIKE @Keyword ESCAPE '\\')";
            parameters.Add("Keyword", $"%{escaped}%");
        }

        sql += $"\nORDER BY p.created_at DESC\nLIMIT {MaxRowsPerTenant}";

        return (sql, parameters);
    }

    private async Task<IReadOnlyList<PublicEventDto>> QueryTenantSafeAsync(
        TenantDto tenant, PortalFilter filter, CancellationToken ct)
    {
        try
        {
            return await QueryTenantPublicEventsAsync(tenant, filter, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTenantQueryFailed(_logger, tenant.Id, ex);
            return [];
        }
    }

    private async Task<IReadOnlyList<PublicEventDto>> QueryTenantPublicEventsAsync(
        TenantDto tenant, PortalFilter filter, CancellationToken ct)
    {
        using var connection = await _tenantConnectionFactory.OpenAsync(tenant.Id, ct);

        var (sql, parameters) = BuildQuery(filter);

        var rows = await connection.QueryAsync<PublicPartyRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return rows.Select(r => new PublicEventDto
        {
            EntityId = r.EntityId,
            Title = r.Title,
            Date = r.Date,
            Description = r.Description,
            TenantId = tenant.Id,
            TenantDisplayName = tenant.DisplayName,
            Type = "Party",
        }).ToList();
    }

    private sealed record PublicPartyRow
    {
        public Guid EntityId { get; init; }

        public string Title { get; init; } = string.Empty;

        public DateTimeOffset Date { get; init; }

        public string? Description { get; init; }
    }
}
