namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Xunit;

/// <summary>
/// Couvre le fournisseur de jeton OAuth 2.0 (client credentials) de Super PDP (F14 §3.1) : échange
/// <c>grant_type=client_credentials</c>, MISE EN CACHE du jeton, renouvellement (expiration / refresh
/// forcé), et propagation LOUD d'un échec d'obtention (jamais un jeton silencieusement vide — CLAUDE.md
/// n°3). Aucune PA réelle : le token-endpoint est mocké.
/// </summary>
public sealed class SuperPdpTokenProviderTests
{
    private static readonly Uri TokenEndpoint = new("https://api.superpdp.tech/oauth2/token");

    [Fact]
    public async Task First_Call_Exchanges_Client_Credentials_And_Returns_The_Access_Token()
    {
        var handler = StubHttpMessageHandler.Returns(
            HttpStatusCode.OK, """{"access_token":"AT-1","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        var token = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        token.Should().Be("AT-1");
        handler.LastRequestUri.Should().Be(TokenEndpoint);
        handler.LastRequestBody.Should().Contain("grant_type=client_credentials");
        handler.LastRequestBody.Should().Contain("client_id=client-FICTIF");
        handler.LastRequestBody.Should().Contain("client_secret=secret-FICTIF");
    }

    [Fact]
    public async Task A_Valid_Cached_Token_Is_Reused_Without_A_Second_Exchange()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.OK, """{"access_token":"AT-CACHED","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        var first = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        first.Should().Be("AT-CACHED");
        second.Should().Be("AT-CACHED");
        handler.PostCount.Should().Be(1, "un jeton encore valide est rendu depuis le cache, sans nouvel échange");
    }

    [Fact]
    public async Task Force_Refresh_Always_Performs_A_New_Exchange()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.OK, """{"access_token":"AT-A","token_type":"bearer","expires_in":1799}""")
            .OnPost(HttpStatusCode.OK, """{"access_token":"AT-B","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var refreshed = await provider.GetAccessTokenAsync(forceRefresh: true, CancellationToken.None);

        refreshed.Should().Be("AT-B");
        handler.PostCount.Should().Be(2, "un refresh forcé ignore le cache (F14 §3.1)");
    }

    [Fact]
    public async Task An_Expired_Token_Is_Renewed_On_The_Next_Call()
    {
        // expires_in 0 → marge de sécurité ⇒ échéance minimale ⇒ jeton considéré déjà expiré (renouvelé
        // au prochain appel) plutôt que de supposer une durée inventée (CLAUDE.md n°2).
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.OK, """{"access_token":"AT-EXPIRED","token_type":"bearer","expires_in":0}""")
            .OnPost(HttpStatusCode.OK, """{"access_token":"AT-FRESH","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var renewed = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        renewed.Should().Be("AT-FRESH");
        handler.PostCount.Should().Be(2, "un jeton expiré est renouvelé sans refresh forcé");
    }

    [Fact]
    public async Task A_Non_Success_Token_Response_Throws_Loud()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.Unauthorized, "{}");
        var provider = CreateProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>(
            "un échec d'obtention de jeton remonte LOUD, jamais un jeton vide (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task A_Response_Without_Access_Token_Throws_Loud()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, """{"token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_Timeout_On_The_Token_Endpoint_Is_Reported_As_Retryable()
    {
        var handler = StubHttpMessageHandler.Throws(new TaskCanceledException("Délai d'attente token simulé."));
        var provider = CreateProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("un timeout token est re-tentable (F14 §3.1)");
    }

    private static SuperPdpTokenProvider CreateProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), TokenEndpoint, "client-FICTIF", "secret-FICTIF");
}
