namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests des 3 familles d'erreurs de l'émission (F14 §4.1 : transitoire / rejet métier / erreur
/// silencieuse), de la relecture d'idempotence anti-doublon (F14 §4.2) et du refresh de jeton OAuth sur
/// 401 (F14 §3.1). Pilotés par <see cref="RoutedHttpMessageHandler"/> — aucune PA réelle.
/// </summary>
public sealed class SuperPdpClientErrorHandlingTests
{
    private const string SilentErrorJson =
        """{"id":"INV-SILENT","state":"issued","errors":[{"code":"VATEX_MISSING","message":"VATEX requis sur une ligne à 0 % (erreur silencieuse)."}]}""";

    [Fact]
    public async Task Silent_Error_On_Http_200_Is_Detected_As_Rejected_Never_Issued()
    {
        var handler = new RoutedHttpMessageHandler().OnPost(HttpStatusCode.OK, SilentErrorJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa, "200 + errors[] = rejet, pas une émission (F14 §4.1)");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("VATEX_MISSING");
        handler.PostCount.Should().Be(1, "un rejet métier (erreur silencieuse) ne déclenche aucune relecture");
    }

    [Fact]
    public async Task Transient_5xx_Without_Reconnect_Degrades_To_Retryable_Technical_Error()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.ServiceUnavailable, """{"errors":[{"code":"SPDP_5XX","message":"Indispo."}]}""")
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "5xx = re-tentable au prochain run (F14 §4.1)");
        handler.PostCount.Should().Be(1, "on ne ré-émet JAMAIS à l'aveugle (anti-doublon)");
        handler.ListCount.Should().Be(1, "une relecture d'idempotence est tentée");
    }

    [Fact]
    public async Task Timeout_Triggers_A_Non_Conclusive_Reconnect_And_Stays_Retryable()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("Délai d'attente Super PDP simulé."))
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        handler.PostCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnect_After_Timeout_Reattaches_An_Already_Created_Invoice(bool wrapped)
    {
        const string number = "F-RECONNECT";
        var listBody = wrapped
            ? SuperPdpTestData.WrappedInvoiceListJsonWith(number)
            : SuperPdpTestData.InvoiceListJsonWith(number);
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("Timeout simulé."))
            .OnListInvoices(HttpStatusCode.OK, listBody);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20(number));

        result.State.Should().Be(PaSendState.Issued, "la facture déjà créée est raccrochée, jamais ré-émise");
        result.PaDocumentId.Should().NotBeNullOrWhiteSpace();
        handler.PostCount.Should().Be(1, "aucune ré-émission : raccrochage par le numéro (F14 §4.2)");
    }

    [Fact]
    public async Task Reconnect_To_An_Unreadable_List_Stays_Technical_Never_Reposts()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.ServiceUnavailable, "{}")
            .OnListInvoices(HttpStatusCode.OK, "<html>not json</html>");
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError,
            "une liste illisible est NON CONCLUANTE — jamais « facture absente », jamais de doublon (CLAUDE.md n°3)");
        handler.PostCount.Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_Refreshes_The_Token_And_Retries_Once()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.Unauthorized, """{"errors":[{"code":"401","message":"Jeton expiré."}]}""")
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler, tokenProvider: tokenProvider);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued, "le 401 a déclenché un refresh + une 2ᵉ tentative réussie");
        tokenProvider.ForceRefreshCount.Should().Be(1, "un 401 force exactement un refresh de jeton (F14 §3.1)");
        handler.PostCount.Should().Be(2, "première tentative 401, seconde avec jeton rafraîchi");
        handler.Requests[1].Authorization!.Parameter.Should().Be(
            StubTokenProvider.RefreshedToken, "la 2ᵉ tentative porte le jeton rafraîchi");
    }

    [Fact]
    public async Task Persistent_Unauthorized_Is_A_Retryable_Auth_Error_Not_A_Business_Rejection()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.Unauthorized, "{}")
            .OnPost(HttpStatusCode.Unauthorized, "{}");
        var client = SuperPdpTestData.CreateClient(handler, tokenProvider: tokenProvider);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError,
            "401 persistant = erreur d'auth/config re-tentable, jamais un rejet métier figé (F14 §4.1)");
        tokenProvider.ForceRefreshCount.Should().Be(1, "un seul refresh, puis on abandonne sans boucler");
        handler.PostCount.Should().Be(2);
    }
}
