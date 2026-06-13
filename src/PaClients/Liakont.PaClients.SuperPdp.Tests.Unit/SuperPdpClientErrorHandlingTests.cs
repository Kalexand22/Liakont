namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests des familles d'erreurs de l'émission (F14 §4.1 : transitoire / rejet métier / échec asynchrone
/// signalé par les <c>events[]</c>), de la relecture d'idempotence anti-doublon (<c>external_id</c>) et
/// du refresh de jeton OAuth sur 401 (F14 §3.1). Pilotés par <see cref="RoutedHttpMessageHandler"/> —
/// aucune PA réelle.
/// </summary>
public sealed class SuperPdpClientErrorHandlingTests
{
    // L'équivalent réel de « l'erreur silencieuse » : 200 transport mais un event api:invalid dans la
    // ressource (échec AVANT transmission — F14 §4.1, O6 levé).
    private const string AsyncFailureJson =
        """{"id":5001,"direction":"out","events":[{"status_code":"api:uploaded","status_text":"Téléversée"},{"status_code":"api:invalid","status_text":"Document invalide avant transmission."}]}""";

    [Fact]
    public async Task Async_Failure_Event_On_Http_200_Is_Detected_As_Rejected_Never_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, AsyncFailureJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.RejectedByPa, "200 + event api:invalid = rejet, pas une émission (F14 §4.1)");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("api:invalid");
        handler.PostCount.Should().Be(1, "un rejet métier ne déclenche aucune relecture");
    }

    [Fact]
    public async Task Transient_5xx_Without_Reconnect_Degrades_To_Retryable_Technical_Error()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.ServiceUnavailable, SuperPdpTestData.ErrorJson(503, "Indisponible."))
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "5xx = re-tentable au prochain run (F14 §4.1)");
        handler.PostCount.Should().Be(1, "on ne ré-émet JAMAIS à l'aveugle (anti-doublon)");
        handler.ListCount.Should().Be(1, "une relecture d'idempotence est tentée");
    }

    [Fact]
    public async Task Transient_5xx_On_Convert_Is_Retryable_And_Never_Posts()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.ServiceUnavailable, SuperPdpTestData.ErrorJson(503, "Indisponible."));
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        // La conversion ne crée RIEN côté PA : son échec transitoire est re-tentable directement, sans
        // relecture d'idempotence et surtout sans émission (F14 §3.2).
        result.State.Should().Be(PaSendState.TechnicalError);
        handler.PostCount.Should().Be(0);
        handler.ListCount.Should().Be(0, "rien n'a pu être créé : aucune relecture nécessaire");
    }

    [Fact]
    public async Task Timeout_Triggers_A_Non_Conclusive_Reconnect_And_Stays_Retryable()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPostThrows(new TaskCanceledException("Délai d'attente Super PDP simulé."))
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        handler.PostCount.Should().Be(1);
    }

    [Fact]
    public async Task Reconnect_After_Timeout_Reattaches_An_Already_Created_Invoice()
    {
        const string number = "F-RECONNECT";
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPostThrows(new TaskCanceledException("Timeout simulé."))
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.InvoiceListJsonWith(number));
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20(number));

        // La facture créée par la tentative qui a expiré porte NOTRE external_id : on la raccroche
        // (état classé par ses events — ici fr:201), on ne ré-émet jamais (F14 §4.1).
        result.State.Should().Be(PaSendState.Issued, "la facture déjà créée est raccrochée, jamais ré-émise");
        result.PaDocumentId.Should().Be("2001");
        handler.PostCount.Should().Be(1, "aucune ré-émission : raccrochage par external_id (F14 §4.1)");
    }

    [Fact]
    public async Task Reconnect_To_An_Unreadable_List_Stays_Technical_Never_Reposts()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.ServiceUnavailable, "{}")
            .OnListInvoices(HttpStatusCode.OK, "<html>not json</html>");
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.TechnicalError,
            "une liste illisible est NON CONCLUANTE — jamais « facture absente », jamais de doublon (CLAUDE.md n°3)");
        handler.PostCount.Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_Refreshes_The_Token_And_Retries_Once()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.Unauthorized, SuperPdpTestData.ErrorJson(401, "Jeton expiré."))
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler, tokenProvider: tokenProvider);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued, "le 401 a déclenché un refresh + une 2ᵉ tentative réussie");
        tokenProvider.ForceRefreshCount.Should().Be(1, "un 401 force exactement un refresh de jeton (F14 §3.1)");
        handler.PostCount.Should().Be(2, "première tentative 401, seconde avec jeton rafraîchi");
        handler.Requests[^1].Authorization!.Parameter.Should().Be(
            StubTokenProvider.RefreshedToken, "la 2ᵉ tentative porte le jeton rafraîchi");
    }

    [Fact]
    public async Task Persistent_Unauthorized_Is_A_Retryable_Auth_Error_Not_A_Business_Rejection()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.Unauthorized, "{}")
            .OnConvert(HttpStatusCode.Unauthorized, "{}");
        var client = SuperPdpTestData.CreateClient(handler, tokenProvider: tokenProvider);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.TechnicalError,
            "401 persistant = erreur d'auth/config re-tentable, jamais un rejet métier figé (F14 §4.1)");
        tokenProvider.ForceRefreshCount.Should().Be(1, "un seul refresh, puis on abandonne sans boucler");
        handler.ConvertCount.Should().Be(2);
        handler.PostCount.Should().Be(0, "aucune émission sans conversion aboutie");
    }
}
