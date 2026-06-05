namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests PAB02 de la relecture d'état <see cref="B2BrouterClient.GetDocumentStatusAsync"/>
/// (<c>GET /invoices/{id}.json</c>, F05 §3). Lecture idempotente → simple retry sur le transitoire,
/// sans garde anti-doublon. Mêmes familles d'erreurs que l'envoi (F05 §4.1).
/// </summary>
public sealed class B2BrouterClientStatusTests
{
    [Fact]
    public async Task GetStatus_Reads_The_Invoice_Endpoint_And_Maps_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-1","state":"issued","tax_report_ids":["TR-1"]}""");
        var client = B2BrouterTestData.CreateClient(handler, accountId: "ACC-7");

        var status = await client.GetDocumentStatusAsync("INV-1");

        status.State.Should().Be(PaSendState.Issued);
        status.PaDocumentId.Should().Be("INV-1");
        status.TaxReportIds.Should().ContainSingle().Which.Should().Be("TR-1");
        status.RawResponse.Should().NotBeNullOrEmpty("la réponse brute est conservée pour l'audit (F06/DR6)");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/invoices/INV-1.json");
    }

    [Fact]
    public async Task GetStatus_Sending_Is_Not_Reported_As_Issued()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-2","state":"sending"}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-2");

        status.State.Should().Be(PaSendState.Sending, "un envoi encore « sending » n'est pas confirmé émis");
    }

    [Fact]
    public async Task GetStatus_200_With_Errors_Is_Rejected()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-3","state":"issued","errors":[{"code":"X","message":"y"}]}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-3");

        status.State.Should().Be(PaSendState.RejectedByPa, "errors[] non vide même sur 200 = rejet (F05 §4.1)");
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("X");
    }

    [Fact]
    public async Task GetStatus_5xx_Is_Retried_Then_Succeeds()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.ServiceUnavailable, "down")
            .OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-1","state":"issued"}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-1");

        status.State.Should().Be(PaSendState.Issued);
        handler.DetailCount.Should().Be(2, "5xx re-tenté puis succès (F05 §4.1)");
    }

    [Fact]
    public async Task GetStatus_5xx_Exhausted_Is_Technical()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.ServiceUnavailable, "down");
        var client = B2BrouterTestData.CreateClient(handler, retryPolicy: B2BrouterRetryPolicy.NoDelay(2));

        var status = await client.GetDocumentStatusAsync("INV-1");

        status.State.Should().Be(PaSendState.TechnicalError);
        handler.DetailCount.Should().Be(3, "tentative initiale + 2 réessais");
    }

    [Fact]
    public async Task GetStatus_Timeout_Exhausted_Is_Technical()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoiceThrows(new TaskCanceledException("délai dépassé"));
        var client = B2BrouterTestData.CreateClient(handler, retryPolicy: B2BrouterRetryPolicy.NoDelay(1));

        var status = await client.GetDocumentStatusAsync("INV-1");

        status.State.Should().Be(PaSendState.TechnicalError);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_TIMEOUT");
        handler.DetailCount.Should().Be(2);
    }

    [Fact]
    public async Task GetStatus_Auth_401_Is_Technical_Without_Retry()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.Unauthorized, string.Empty);
        var client = B2BrouterTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-1");

        status.State.Should().Be(PaSendState.TechnicalError, "401 = config/auth re-tentable, jamais ré-essayée en boucle (F05 §4.1)");
        handler.DetailCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatus_404_Is_Rejected_With_Http_Code()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.NotFound, string.Empty);
        var client = B2BrouterTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-1");

        status.State.Should().Be(PaSendState.RejectedByPa);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("404");
        handler.DetailCount.Should().Be(1, "un 404 ne se retente pas (F05 §4.1)");
    }

    [Fact]
    public async Task GetStatus_Caller_Cancellation_Propagates_Without_Calling_The_Pa()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-1","state":"issued"}""");
        var client = B2BrouterTestData.CreateClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => client.GetDocumentStatusAsync("INV-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.DetailCount.Should().Be(0);
    }
}
