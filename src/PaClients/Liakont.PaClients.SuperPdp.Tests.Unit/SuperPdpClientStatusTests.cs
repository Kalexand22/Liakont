namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests de la relecture d'état (polling, F14 §3.4) : <c>GET /v1.beta/invoices/{id}</c> avec retry sur le
/// transitoire (5xx/réseau/timeout) et classification cohérente avec l'émission. Pilotés par
/// <see cref="RoutedHttpMessageHandler"/> — aucune PA réelle.
/// </summary>
public sealed class SuperPdpClientStatusTests
{
    [Fact]
    public async Task Status_Maps_Issued_And_Targets_The_Document_Endpoint()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-7","state":"issued","tax_report_ids":["TR-7"]}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-7");

        status.State.Should().Be(PaSendState.Issued);
        status.TaxReportIds.Should().ContainSingle().Which.Should().Be("TR-7");
        handler.Requests.Should().ContainSingle()
            .Which.Path.Should().Be("/v1.beta/invoices/INV-7");
    }

    [Fact]
    public async Task Status_Maps_Sending_As_In_Progress_Never_Issued()
    {
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-8","state":"sending"}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-8");

        status.State.Should().Be(PaSendState.Sending);
    }

    [Fact]
    public async Task Status_With_Errors_On_200_Is_Rejected()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-9","errors":[{"code":"X","message":"y"}]}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-9");

        status.State.Should().Be(PaSendState.RejectedByPa);
        status.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Status_Retries_Transient_5xx_Then_Succeeds()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnGetInvoice(HttpStatusCode.ServiceUnavailable, "{}")
            .OnGetInvoice(HttpStatusCode.OK, """{"id":"INV-10","state":"issued"}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-10");

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
        var handler = new RoutedHttpMessageHandler().OnGetInvoice(HttpStatusCode.NotFound, "{}");
        var client = SuperPdpTestData.CreateClient(handler);

        var status = await client.GetDocumentStatusAsync("INV-404");

        status.State.Should().Be(PaSendState.RejectedByPa);
        handler.DetailCount.Should().Be(1, "un 404 n'est pas re-tentable (F14 §4.1)");
    }
}
