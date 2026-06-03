namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

public sealed class KeycloakAdminTokenServiceTests : IDisposable
{
    private readonly KeycloakAdminOptions _options = new()
    {
        AdminBaseUrl = "http://localhost:8080",
        AdminUsername = "admin",
        AdminPassword = "admin-secret",
    };

    private readonly FakeHttpMessageHandler _handler = new();
    private KeycloakAdminTokenService? _sut;

    public void Dispose()
    {
        _sut?.Dispose();
    }

    [Fact]
    public async Task GetTokenAsync_Should_AcquireToken_On_FirstCall()
    {
        SetupTokenResponse("test-access-token", expiresIn: 300);
        var sut = CreateSut();

        var token = await sut.GetTokenAsync();

        Assert.Equal("test-access-token", token);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_Should_ReturnCachedToken_When_NotExpired()
    {
        SetupTokenResponse("cached-token", expiresIn: 300);
        var sut = CreateSut();

        var token1 = await sut.GetTokenAsync();
        var token2 = await sut.GetTokenAsync();

        Assert.Equal("cached-token", token1);
        Assert.Equal("cached-token", token2);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_Should_SendCorrectGrantType()
    {
        SetupTokenResponse("token", expiresIn: 300);
        var sut = CreateSut();

        await sut.GetTokenAsync();

        Assert.NotNull(_handler.LastRequestContent);
        var body = await _handler.LastRequestContent.ReadAsStringAsync();
        Assert.Contains("grant_type=password", body);
        Assert.Contains("client_id=admin-cli", body);
        Assert.Contains("username=admin", body);
        Assert.Contains("password=admin-secret", body);
    }

    [Fact]
    public async Task GetTokenAsync_Should_CallCorrectUrl()
    {
        SetupTokenResponse("token", expiresIn: 300);
        var sut = CreateSut();

        await sut.GetTokenAsync();

        Assert.Equal(
            "http://localhost:8080/realms/master/protocol/openid-connect/token",
            _handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task GetTokenAsync_Should_ThrowOnHttpFailure()
    {
        _handler.SetupResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var sut = CreateSut();

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetTokenAsync());
    }

    [Fact]
    public async Task GetTokenAsync_Should_ThrowOnNullBody()
    {
        _handler.SetupResponse(HttpStatusCode.OK, "null");
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetTokenAsync());
    }

    [Fact]
    public async Task GetTokenAsync_Should_RefreshToken_When_WithinExpiryBuffer()
    {
        // expiresIn=31 means the token expires in 31s from now.
        // With a 30s buffer, it's considered valid for only ~1s.
        // First call acquires, second call should still be cached (we call immediately).
        // We can at least verify the mechanism works by using expiresIn=0 (already expired).
        SetupTokenResponse("first-token", expiresIn: 0);
        var sut = CreateSut();

        var token1 = await sut.GetTokenAsync();
        Assert.Equal("first-token", token1);

        // Token with expiresIn=0 is already within the 30s buffer, so next call refreshes
        SetupTokenResponse("second-token", expiresIn: 300);
        var token2 = await sut.GetTokenAsync();

        Assert.Equal("second-token", token2);
        Assert.Equal(2, _handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_Should_TrimTrailingSlashFromBaseUrl()
    {
        var handler = new FakeHttpMessageHandler();
        var json = JsonSerializer.Serialize(new
        {
            access_token = "token",
            expires_in = 300,
            token_type = "Bearer",
        });
        handler.SetupResponse(HttpStatusCode.OK, json);

        var options = new KeycloakAdminOptions
        {
            AdminBaseUrl = "http://localhost:8080/",
            AdminUsername = "admin",
            AdminPassword = "admin-secret",
        };
        var factory = new FakeHttpClientFactory(handler);
        using var sut = new KeycloakAdminTokenService(
            factory, Options.Create(options), NullLogger<KeycloakAdminTokenService>.Instance);

        await sut.GetTokenAsync();

        Assert.Equal(
            "http://localhost:8080/realms/master/protocol/openid-connect/token",
            handler.LastRequestUri?.ToString());
    }

    private KeycloakAdminTokenService CreateSut()
    {
        var factory = new FakeHttpClientFactory(_handler);
        _sut = new KeycloakAdminTokenService(
            factory,
            Options.Create(_options),
            NullLogger<KeycloakAdminTokenService>.Instance);
        return _sut;
    }

    private void SetupTokenResponse(string accessToken, int expiresIn)
    {
        var json = JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            expires_in = expiresIn,
            token_type = "Bearer",
        });
        _handler.SetupResponse(HttpStatusCode.OK, json);
    }
}
