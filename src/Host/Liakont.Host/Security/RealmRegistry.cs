namespace Liakont.Host.Security;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Thread-safe, in-memory registry of Keycloak realms and their tenant mappings.
/// Seeded at startup from config + DB, updated at runtime on tenant provisioning.
/// </summary>
internal sealed partial class RealmRegistry : IRealmRegistry
{
    private readonly ConcurrentDictionary<string, string> _realmToTenant = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _knownIssuers = new(StringComparer.OrdinalIgnoreCase);
    private readonly MultiRealmJwksKeyResolver _jwksKeyResolver;
    private readonly ILogger<RealmRegistry> _logger;

    public RealmRegistry(
        MultiRealmJwksKeyResolver jwksKeyResolver,
        ILogger<RealmRegistry> logger)
    {
        _jwksKeyResolver = jwksKeyResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsKnownIssuer(string issuer)
    {
        var normalized = issuer.TrimEnd('/');
        return _knownIssuers.ContainsKey(normalized);
    }

    /// <inheritdoc />
    public bool TryGetTenantId(string realmName, out string? tenantId)
    {
        return _realmToTenant.TryGetValue(realmName, out tenantId);
    }

    /// <inheritdoc />
    public void RegisterRealm(string realmName, string tenantId, string authority)
    {
        var normalizedAuthority = authority.TrimEnd('/');

        _realmToTenant[realmName] = tenantId;
        _knownIssuers[normalizedAuthority] = 0;
        _jwksKeyResolver.AddAuthority(normalizedAuthority);

        LogRealmRegistered(_logger, realmName, tenantId, normalizedAuthority);
    }

    /// <inheritdoc />
    public void UnregisterRealm(string realmName, string authority)
    {
        var normalizedAuthority = authority.TrimEnd('/');

        _realmToTenant.TryRemove(realmName, out _);
        _knownIssuers.TryRemove(normalizedAuthority, out _);

        LogRealmUnregistered(_logger, realmName, normalizedAuthority);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Realm '{RealmName}' registered: tenant='{TenantId}', authority='{Authority}'")]
    private static partial void LogRealmRegistered(ILogger logger, string realmName, string tenantId, string authority);

    [LoggerMessage(Level = LogLevel.Information, Message = "Realm '{RealmName}' unregistered: authority='{Authority}'")]
    private static partial void LogRealmUnregistered(ILogger logger, string realmName, string authority);
}
