namespace Stratum.Common.Abstractions.Caching;

/// <summary>
/// Abstraction for a distributed-compatible cache with mandatory TTL.
/// Default implementation uses in-process memory; swap for Redis/etc. without changing callers.
/// </summary>
public interface ICacheService
{
    /// <summary>Returns the cached value, or null if absent or expired.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        where T : class;

    /// <summary>Stores a value with a mandatory time-to-live.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class;

    /// <summary>Removes a single entry by exact key.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Removes all entries whose key starts with <paramref name="prefix"/>.</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
