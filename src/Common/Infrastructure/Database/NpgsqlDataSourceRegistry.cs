namespace Stratum.Common.Infrastructure.Database;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Npgsql;

/// <summary>
/// Manages a pool of <see cref="NpgsqlDataSource"/> instances keyed by tenant ID.
/// Each tenant gets its own <see cref="NpgsqlDataSource"/> (and thus its own connection pool),
/// created lazily on first access and reused for subsequent calls.
/// <para>
/// Implements <see cref="IAsyncDisposable"/> to dispose all data sources on application shutdown.
/// </para>
/// <para>
/// Connection strings are captured at first registration for a given key and cannot be changed.
/// If a different connection string is passed for an existing key, a warning is logged and the
/// original data source is returned unchanged.
/// </para>
/// </summary>
public sealed partial class NpgsqlDataSourceRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, RegistryEntry> _entries = new(StringComparer.Ordinal);
    private readonly ILogger<NpgsqlDataSourceRegistry> _logger;
    private int _disposed;

    public NpgsqlDataSourceRegistry(ILogger<NpgsqlDataSourceRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the number of data sources currently held by the registry.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns an existing <see cref="NpgsqlDataSource"/> for the given key, or creates one
    /// lazily using the supplied connection string. Thread-safe: concurrent calls for the same
    /// key will share a single data source.
    /// </summary>
    /// <param name="key">Tenant identifier (or "system" for the default connection).</param>
    /// <param name="connectionString">PostgreSQL connection string used if a new data source must be created.
    /// Once a key is registered, subsequent calls with a different connection string are ignored (with a warning log).</param>
    /// <returns>A reusable <see cref="NpgsqlDataSource"/> with its own connection pool.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public NpgsqlDataSource GetOrCreate(string key, string connectionString)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Single atomic GetOrAdd ensures connection string and data source are co-located.
        var entry = _entries.GetOrAdd(key, k => new RegistryEntry(connectionString, new Lazy<NpgsqlDataSource>(() =>
        {
            // Double-check disposal inside the lazy factory to prevent creating data sources
            // after DisposeAsync has started (TOCTOU guard).
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, nameof(NpgsqlDataSourceRegistry));

            LogDataSourceCreated(_logger, k);
            return NpgsqlDataSource.Create(connectionString);
        })));

        // Detect connection string mismatch (key already existed with a different CS)
        if (!string.Equals(entry.ConnectionString, connectionString, StringComparison.Ordinal))
        {
            LogConnectionStringMismatch(_logger, key);
        }

        return entry.DataSource.Value;
    }

    /// <summary>
    /// Disposes all <see cref="NpgsqlDataSource"/> instances held by the registry.
    /// After disposal, <see cref="GetOrCreate"/> will throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Atomic check-and-set: only one caller proceeds with disposal.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        LogDisposingAll(_logger, _entries.Count);

        foreach (var kvp in _entries)
        {
            if (kvp.Value.DataSource.IsValueCreated)
            {
                try
                {
                    await kvp.Value.DataSource.Value.DisposeAsync();
                    LogDataSourceDisposed(_logger, kvp.Key);
                }
                catch (Exception ex)
                {
                    LogDataSourceDisposeError(_logger, kvp.Key, ex);
                }
            }
        }

        _entries.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created NpgsqlDataSource for tenant '{Key}'")]
    private static partial void LogDataSourceCreated(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disposing all NpgsqlDataSources ({Count} registered)")]
    private static partial void LogDisposingAll(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Disposed NpgsqlDataSource for tenant '{Key}'")]
    private static partial void LogDataSourceDisposed(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error disposing NpgsqlDataSource for tenant '{Key}'")]
    private static partial void LogDataSourceDisposeError(ILogger logger, string key, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GetOrCreate called for key '{Key}' with a different connection string than originally registered — using original data source")]
    private static partial void LogConnectionStringMismatch(ILogger logger, string key);

    private sealed record RegistryEntry(string ConnectionString, Lazy<NpgsqlDataSource> DataSource);
}
