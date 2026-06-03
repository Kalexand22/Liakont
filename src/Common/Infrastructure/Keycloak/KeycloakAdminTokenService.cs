namespace Stratum.Common.Infrastructure.Keycloak;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Acquires and caches admin access tokens from the Keycloak master realm
/// using the Resource Owner Password Credentials grant.
/// </summary>
internal sealed partial class KeycloakAdminTokenService : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeycloakAdminOptions _options;
    private readonly ILogger<KeycloakAdminTokenService> _logger;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public KeycloakAdminTokenService(
        IHttpClientFactory httpClientFactory,
        IOptions<KeycloakAdminOptions> options,
        ILogger<KeycloakAdminTokenService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns a valid admin access token, refreshing if expired.
    /// </summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: token still valid (with 30s buffer)
        if (_cachedToken is not null && DateTimeOffset.UtcNow.AddSeconds(30) < _tokenExpiry)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTimeOffset.UtcNow.AddSeconds(30) < _tokenExpiry)
            {
                return _cachedToken;
            }

            return await AcquireTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keycloak admin token acquired (expires in {ExpiresIn}s)")]
    private static partial void LogTokenAcquired(ILogger logger, int expiresIn);

    private async Task<string> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        var tokenUrl = $"{_options.AdminBaseUrl.TrimEnd('/')}/realms/master/protocol/openid-connect/token";

        var client = _httpClientFactory.CreateClient("KeycloakAdmin");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = _options.AdminUsername,
            ["password"] = _options.AdminPassword,
        });

        var response = await client.PostAsync(tokenUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize Keycloak token response.");

        _cachedToken = tokenResponse.AccessToken;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        LogTokenAcquired(_logger, tokenResponse.ExpiresIn);

        return _cachedToken;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = default!;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
