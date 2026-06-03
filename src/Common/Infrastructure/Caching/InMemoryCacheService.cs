namespace Stratum.Common.Infrastructure.Caching;

using System.Collections.Concurrent;
using System.Text.Json;
using Stratum.Common.Abstractions.Caching;

/// <summary>
/// In-process cache backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Entries expire passively on access. No background sweep thread.
/// TTL is mandatory on every write.
/// </summary>
internal sealed class InMemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        where T : class
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow < entry.ExpiresAt)
            {
                try
                {
                    return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Json));
                }
                catch (JsonException)
                {
                    _store.TryRemove(key, out _);
                    return Task.FromResult<T?>(null);
                }
            }

            _store.TryRemove(key, out _);
        }

        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be greater than zero.");
        }

        var json = JsonSerializer.Serialize(value);
        var expires = DateTimeOffset.UtcNow.Add(ttl);
        _store[key] = new CacheEntry(json, expires);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            throw new ArgumentException("Prefix must not be empty.", nameof(prefix));
        }

        foreach (var key in _store.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _store.TryRemove(key, out _);
            }
        }

        return Task.CompletedTask;
    }

    private sealed record CacheEntry(string Json, DateTimeOffset ExpiresAt);
}
