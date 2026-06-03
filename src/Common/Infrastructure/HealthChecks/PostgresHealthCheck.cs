namespace Stratum.Common.Infrastructure.HealthChecks;

using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Verifies PostgreSQL connectivity by executing a lightweight SELECT 1 query.
/// </summary>
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly ISystemConnectionFactory _connectionFactory;

    public PostgresHealthCheck(ISystemConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await connection.ExecuteScalarAsync<int>("SELECT 1");
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
