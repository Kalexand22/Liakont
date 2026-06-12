namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests de la relecture d'état (polling, F14 §3.4) : <c>GET /v1.beta/invoices/{id}</c> avec retry sur le
/// transitoire (5xx/réseau/timeout) et classification par les <c>events[]</c> (✅ contrat confirmé sandbox
/// 2026-06-12), cohérente avec l'émission. Pilotés par <see cref="RoutedHttpMessageHandler"/> — aucune PA
/// réelle.
/// </summary>
public sealed class SuperPdpClientStatusTests
{
    [Fact]
    public async Task Status_Maps_Fr201_To_Issued_And_Targets_The_Document_Endpoint()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("1001");

        status.State.Should().Be(PaSendState.Issued, "fr:201 « Émise par la plateforme » vaut émission (F14 §4.1)");
        status.PaDocumentId.Should().Be("1001");
        handler.Requests.Should().ContainSingle()
            .Which.Path.Should().Be("/v1.beta/invoices/1001");
    }

    [Fact]
    public async Task Status_Uploaded_Only_Is_In_Progress_Never_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.OK, SuperPdpTestData.UploadedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("1002");

        status.State.Should().Be(
            PaSendState.Sending,
            "api:uploaded seul = téléversée, l'émission n'est pas confirmée — jamais « émis » par défaut (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task Status_With_A_Failure_Event_Is_Rejected_With_The_Event_Intact()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(
                HttpStatusCode.OK,
                """{"id":4001,"direction":"out","events":[{"status_code":"api:uploaded","status_text":"Téléversée"},{"status_code":"fr:213","status_text":"Rejetée"}]}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("4001");

        // fr:213 (Rejetée) est un échec terminal SANS émission : prioritaire sur tout le reste (F14 §4.1).
        status.State.Should().Be(PaSendState.RejectedByPa);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("fr:213");
    }

    [Fact]
    public async Task Status_Unknown_Event_Code_Stays_In_Progress_Never_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(
                HttpStatusCode.OK,
                """{"id":4002,"direction":"out","events":[{"status_code":"xx:999","status_text":"Code inconnu"}]}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("4002");

        status.State.Should().Be(
            PaSendState.Sending,
            "un code d'événement inconnu reste « en cours » — jamais « émis » par défaut (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task Status_Retries_Transient_5xx_Then_Succeeds()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.ServiceUnavailable, "{}")
            .OnGetInvoice(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("1001");

        status.State.Should().Be(PaSendState.Issued);
        handler.DetailCount.Should().Be(2, "un 5xx est ré-essayé puis la lecture aboutit");
    }

    [Fact]
    public async Task Status_Exhausts_Retries_On_Persistent_5xx_And_Stays_Technical()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.ServiceUnavailable, "{}");
        var client = SuperPdpTestData.CreateClient(handler, retryPolicy: SuperPdpRetryPolicy.NoDelay(2));

        var status = await client.GetDocumentStatusAsync("INV-11");

        status.State.Should().Be(PaSendState.TechnicalError, "5xx persistant reste re-tentable au prochain run");
        handler.DetailCount.Should().Be(3, "tentative initiale + 2 réessais (NoDelay(2))");
    }

    [Fact]
    public async Task Status_404_Is_A_Document_Level_Rejection_Not_Retried()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.NotFound, SuperPdpTestData.ErrorJson(404, "Facture inconnue."));
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-404");

        status.State.Should().Be(PaSendState.RejectedByPa);
        status.Errors.Should().ContainSingle().Which.Message.Should().Be("Facture inconnue.");
        handler.DetailCount.Should().Be(1, "un 404 n'est pas re-tentable (F14 §4.1)");
    }
}
