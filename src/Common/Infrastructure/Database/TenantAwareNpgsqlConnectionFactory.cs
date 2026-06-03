namespace Stratum.Common.Infrastructure.Database;

using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

/// <summary>
/// Opens tenant-scoped PostgreSQL connections using database-per-tenant isolation.
/// <list type="bullet">
///   <item>Per-tenant DB (default): derives the connection string from the default
///         by changing the <c>Database</c> to <c>{DatabasePrefix}{tenantId}</c>.</item>
///   <item>Per-tenant override (opt-in via config): uses an explicit connection string
///         from <see cref="TenantConnectionOptions.ConnectionStrings"/>.</item>
///   <item>System tenant (<c>null</c>): uses the default connection string with no
///         override.</item>
/// </list>
/// Connections are obtained from <see cref="NpgsqlDataSourceRegistry"/>, which manages
/// a pool of <see cref="NpgsqlDataSource"/> instances keyed by tenant.
/// </summary>
public sealed partial class TenantAwareNpgsqlConnectionFactory : ITenantConnectionFactory
{
    /// <summary>
    /// PostgreSQL maximum identifier length.
    /// </summary>
    private const int MaxPgIdentifierLength = 63;

    /// <summary>
    /// Key used in the data source registry for the system (no-tenant) connection.
    /// </summary>
    private const string SystemDataSourceKey = "__system__";

    private readonly string _defaultConnectionString;
    private readonly string _databasePrefix;
    private readonly TenantConnectionOptions _tenantOptions;
    private readonly NpgsqlDataSourceRegistry _dataSourceRegistry;
    private readonly ILogger<TenantAwareNpgsqlConnectionFactory> _logger;

    public TenantAwareNpgsqlConnectionFactory(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<TenantConnectionOptions> tenantOptions,
        NpgsqlDataSourceRegistry dataSourceRegistry,
        ILogger<TenantAwareNpgsqlConnectionFactory> logger)
    {
        _defaultConnectionString = databaseOptions.Value.ConnectionString;
        _tenantOptions = tenantOptions.Value;
        _databasePrefix = _tenantOptions.DatabasePrefix;
        _dataSourceRegistry = dataSourceRegistry;
        _logger = logger;
    }

    public async Task<IDbConnection> OpenAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        // System tenant — default connection, no override
        if (string.IsNullOrEmpty(tenantId))
        {
            LogSystemConnection(_logger);
            return await OpenConnectionFromRegistryAsync(SystemDataSourceKey, _defaultConnectionString, cancellationToken);
        }

        ValidateTenantId(tenantId);

        // Check for per-tenant connection string override
        if (_tenantOptions.ConnectionStrings.TryGetValue(tenantId, out var tenantConnectionString))
        {
            LogPerTenantConnection(_logger, tenantId);
            return await OpenConnectionFromRegistryAsync(tenantId, tenantConnectionString, cancellationToken);
        }

        // Database-per-tenant: derive connection string by changing the database name
        var databaseName = BuildDatabaseName(tenantId);
        LogDerivedDatabaseConnection(_logger, tenantId, databaseName);
        var derivedConnectionString = BuildTenantConnectionString(_defaultConnectionString, databaseName);
        return await OpenConnectionFromRegistryAsync(tenantId, derivedConnectionString, cancellationToken);
    }

    private static string BuildTenantConnectionString(string baseConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };
        return builder.ToString();
    }

    private static void ValidateTenantId(string tenantId)
    {
        if (!TenantIdRegex().IsMatch(tenantId))
        {
            throw new ArgumentException(
                $"Invalid tenant ID format: '{tenantId}'. Must be 1-63 lowercase alphanumeric characters or hyphens, starting and ending with a letter or digit.",
                nameof(tenantId));
        }
    }

    // Tenant ID: alphanumeric + hyphens, 1-63 chars, starts/ends with letter/digit.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9\-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex TenantIdRegex();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opening system connection (no tenant)")]
    private static partial void LogSystemConnection(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opening connection for tenant {TenantId} using per-tenant connection string")]
    private static partial void LogPerTenantConnection(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opening connection for tenant {TenantId} using derived database '{DatabaseName}'")]
    private static partial void LogDerivedDatabaseConnection(ILogger logger, string tenantId, string databaseName);

    private async Task<NpgsqlConnection> OpenConnectionFromRegistryAsync(
        string key, string connectionString, CancellationToken cancellationToken)
    {
        var dataSource = _dataSourceRegistry.GetOrCreate(key, connectionString);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    private string BuildDatabaseName(string tenantId)
    {
        var databaseName = $"{_databasePrefix}{tenantId.Replace('-', '_')}";

        if (databaseName.Length > MaxPgIdentifierLength)
        {
            throw new InvalidOperationException(
                $"Composed database name '{databaseName}' exceeds PostgreSQL's {MaxPgIdentifierLength}-character identifier limit. "
                + "Shorten the database prefix or tenant ID.");
        }

        return databaseName;
    }
}
