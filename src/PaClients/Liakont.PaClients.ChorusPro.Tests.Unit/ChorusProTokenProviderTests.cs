namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Net;
using FluentAssertions;
using Xunit;

/// <summary>
/// Couvre le fournisseur de jeton OAuth2 PISTE (F18 §2.1) : échange <c>grant_type=client_credentials</c>
/// AVEC <c>scope=openid</c> (ajout PISTE), MISE EN CACHE du jeton, renouvellement piloté sur l'<c>expires_in</c>
/// RÉEL (jamais « 3600 s » figé, jamais de <c>refresh_token</c> → re-échange), et propagation LOUD d'un
/// échec (jamais un jeton silencieusement vide — CLAUDE.md n°2/3). Aucune PA réelle : le token-endpoint est
/// mocké.
/// </summary>
public sealed class ChorusProTokenProviderTests
{
    private static readonly Uri TokenEndpoint = new("https://sandbox-oauth.piste.gouv.fr/api/oauth/token");

    [Fact]
    public async Task First_Call_Exchanges_Client_Credentials_With_Openid_Scope_And_Returns_The_Token()
    {
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-1","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        var token = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        token.Should().Be("AT-1");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be(TokenEndpoint);

        var body = handler.Requests[0].Body!;
        body.Should().Contain("grant_type=client_credentials");
        body.Should().Contain("client_id=client-FICTIF");
        body.Should().Contain("client_secret=secret-FICTIF");
        body.Should().Contain("scope=openid", "le scope=openid est l'ajout PISTE au client_credentials (F18 §2.1)");
    }

    [Fact]
    public async Task A_Valid_Cached_Token_Is_Reused_Without_A_Second_Exchange()
    {
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-CACHED","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        var first = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        first.Should().Be("AT-CACHED");
        second.Should().Be("AT-CACHED");
        handler.CallCount.Should().Be(1, "un jeton encore valide est rendu depuis le cache, sans nouvel échange");
    }

    [Fact]
    public async Task Force_Refresh_Always_Performs_A_New_Exchange_There_Is_No_Refresh_Token()
    {
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-A","token_type":"bearer","expires_in":1799}""")
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-B","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var refreshed = await provider.GetAccessTokenAsync(forceRefresh: true, CancellationToken.None);

        refreshed.Should().Be("AT-B");
        handler.CallCount.Should().Be(2, "PISTE n'émet pas de refresh_token : un refresh est un re-échange client_credentials (F18 §2.1)");
    }

    [Fact]
    public async Task An_Expired_Token_Is_Renewed_On_The_Next_Call_Driven_By_The_Real_Expires_In()
    {
        // expires_in 0 → marge de sécurité ⇒ échéance minimale ⇒ jeton considéré déjà expiré (renouvelé au
        // prochain appel) plutôt que de supposer une durée inventée comme « 3600 s » (CLAUDE.md n°2).
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-EXPIRED","token_type":"bearer","expires_in":0}""")
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-FRESH","token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var renewed = await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        renewed.Should().Be("AT-FRESH");
        handler.CallCount.Should().Be(2, "un jeton expiré est renouvelé sans refresh forcé, sur l'expires_in réel");
    }

    [Fact]
    public async Task A_Non_Success_Token_Response_Throws_Loud_Without_Leaking_The_Secret()
    {
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.Unauthorized, "{}");
        var provider = CreateProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>(
            "un échec d'obtention de jeton remonte LOUD, jamais un jeton vide (CLAUDE.md n°3)")).Which;
        thrown.Message.Should().NotContain("secret-FICTIF", "aucun secret PISTE n'apparaît dans un message d'exception (CLAUDE.md n°10)");
        thrown.Message.Should().NotContain("client-FICTIF");
    }

    [Fact]
    public async Task A_Response_Without_Access_Token_Throws_Loud()
    {
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """{"token_type":"bearer","expires_in":1799}""");
        var provider = CreateProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_Timeout_On_The_Token_Endpoint_Is_Reported_As_Retryable()
    {
        var handler = new RecordingHttpMessageHandler().Throws(new TaskCanceledException("Délai d'attente token simulé."));
        var provider = CreateProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("un timeout token est re-tentable (F18 §2.1)");
    }

    private static ChorusProTokenProvider CreateProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), TokenEndpoint, "client-FICTIF", "secret-FICTIF", ChorusProDefaults.TokenScope);
}
