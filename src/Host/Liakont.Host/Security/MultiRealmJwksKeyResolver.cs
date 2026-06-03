namespace Liakont.Host.Security;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Resolves signing keys from multiple Keycloak realm JWKS endpoints.
/// Each realm has its own signing keys; this resolver caches and refreshes
/// them per-issuer to support multi-realm JwtBearer validation.
/// </summary>
/// <remarks>
/// Filters by token issuer to only resolve keys from the matching realm's JWKS endpoint,
/// preventing cross-realm key leakage and reducing unnecessary network calls.
/// <see cref="ConfigurationManager{T}"/> handles caching and background refresh automatically.
/// </remarks>
internal sealed partial class MultiRealmJwksKeyResolver
{
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MultiRealmJwksKeyResolver>? _logger;

    private readonly bool _requireHttpsMetadata;

    public MultiRealmJwksKeyResolver(
        IEnumerable<string> authorities,
        bool requireHttpsMetadata,
        ILogger<MultiRealmJwksKeyResolver>? logger = null)
    {
        _logger = logger;
        _requireHttpsMetadata = requireHttpsMetadata;

        foreach (var authority in authorities)
        {
            AddAuthority(authority);
        }
    }

    /// <summary>
    /// Registers a new authority at runtime so that JWTs from this issuer
    /// can be validated without an application restart.
    /// </summary>
    public void AddAuthority(string authority)
    {
        var normalized = authority.TrimEnd('/');
        if (_managers.ContainsKey(normalized))
        {
            return;
        }

        var metadataAddress = normalized + "/.well-known/openid-configuration";
        _managers[normalized] = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = _requireHttpsMetadata });
    }

    public IEnumerable<SecurityKey> ResolveSigningKeys(
        string token,
        SecurityToken securityToken,
        string kid,
        TokenValidationParameters validationParameters)
    {
        // Extract issuer from the token — supports both JsonWebToken (.NET 9+) and legacy JwtSecurityToken
        var issuer = securityToken switch
        {
            JsonWebToken jwt => jwt.Issuer,
            JwtSecurityToken jwtSec => jwtSec.Issuer,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(issuer))
        {
            return [];
        }

        var normalizedIssuer = issuer.TrimEnd('/');

        if (!_managers.TryGetValue(normalizedIssuer, out var manager))
        {
            if (_logger is not null)
            {
                LogNoManagerForIssuer(_logger, issuer);
            }

            return [];
        }

        try
        {
            var config = manager.GetConfigurationAsync(CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            return config.SigningKeys;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogJwksFetchFailed(_logger, issuer, ex);
            }

            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No JWKS manager found for issuer {Issuer}")]
    private static partial void LogNoManagerForIssuer(ILogger logger, string issuer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to retrieve JWKS from {Issuer}")]
    private static partial void LogJwksFetchFailed(ILogger logger, string issuer, Exception ex);
}
