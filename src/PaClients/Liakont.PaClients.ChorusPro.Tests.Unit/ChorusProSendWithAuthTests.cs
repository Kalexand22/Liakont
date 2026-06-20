namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Net;
using FluentAssertions;
using Xunit;

/// <summary>
/// Couvre la DOUBLE authentification PISTE de CP03 (F18 §2) portée par
/// <see cref="ChorusProClient.SendWithAuthAsync"/> : chaque requête métier porte SIMULTANÉMENT le Bearer
/// PISTE ET l'en-tête <c>cpro-account</c> du compte technique ; un <c>401</c> déclenche UN refresh de jeton
/// (forceRefresh) + UNE seule re-tentative (patron SuperPdpClient). Aucune PA réelle : l'HTTP est mocké, le
/// jeton est fourni par un stub.
/// </summary>
public sealed class ChorusProSendWithAuthTests
{
    private const string TechnicalAccountHeader = "bG9naW46bWRw"; // base64("login:mdp") — valeur de test fictive.

    private static readonly Uri BusinessUri = new("https://sandbox-api.piste.gouv.fr/cpro/factures/deposer");

    [Fact]
    public async Task Each_Request_Carries_Both_The_Bearer_And_The_Cpro_Account_Header()
    {
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK);
        var tokenProvider = new StubChorusProTokenProvider();
        var client = NewClient(handler, tokenProvider);

        using var response = await client.SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Post, BusinessUri), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Requests.Should().ContainSingle();

        var sent = handler.Requests[0];
        sent.Authorization!.Scheme.Should().Be("Bearer");
        sent.Authorization.Parameter.Should().Be(StubChorusProTokenProvider.NominalToken);
        sent.TechnicalAccount.Should().Be(TechnicalAccountHeader, "le cpro-account du compte technique est posé à CHAQUE requête (F18 §2.2)");
    }

    [Fact]
    public async Task A_401_Triggers_One_Token_Refresh_And_A_Single_Retry()
    {
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.Unauthorized)
            .Respond(HttpStatusCode.OK);
        var tokenProvider = new StubChorusProTokenProvider();
        var client = NewClient(handler, tokenProvider);

        using var response = await client.SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Post, BusinessUri), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2, "un 401 déclenche exactement UNE re-tentative (F18 §2.1, patron SuperPdp)");
        tokenProvider.ForceRefreshCount.Should().Be(1, "le jeton est redemandé avec forceRefresh après le 401");

        handler.Requests[0].Authorization!.Parameter.Should().Be(StubChorusProTokenProvider.NominalToken);
        handler.Requests[1].Authorization!.Parameter.Should().Be(
            StubChorusProTokenProvider.RefreshedToken, "la re-tentative utilise le jeton rafraîchi");
        handler.Requests[1].TechnicalAccount.Should().Be(TechnicalAccountHeader, "le cpro-account est re-posé sur la re-tentative");
    }

    [Fact]
    public async Task A_Second_401_Is_Returned_To_The_Caller_Without_A_Third_Attempt()
    {
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.Unauthorized)
            .Respond(HttpStatusCode.Unauthorized);
        var client = NewClient(handler, new StubChorusProTokenProvider());

        using var response = await client.SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Post, BusinessUri), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.CallCount.Should().Be(2, "le retry est borné à UNE re-tentative — le second 401 remonte tel quel (classé par le mapper CP04+)");
    }

    [Fact]
    public async Task SendWithAuth_With_Null_Build_Throws()
    {
        var client = NewClient(new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK), new StubChorusProTokenProvider());

        var act = async () => await client.SendWithAuthAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static ChorusProClient NewClient(HttpMessageHandler handler, IChorusProTokenProvider tokenProvider) =>
        new(new HttpClient(handler), tokenProvider, TechnicalAccountHeader, ChorusProCapabilities.Declared);
}
