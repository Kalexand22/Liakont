namespace Liakont.Agent.Core.Tests.Transport;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Transport;
using Xunit;

/// <summary>
/// Client HTTP du contrat d'ingestion (F12 §3) testé contre un gestionnaire HTTP mocké : en-têtes
/// d'authentification, corps de lot (JSON canonique + régimes), et traduction de TOUS les codes de
/// réponse F12 §3.3 en <see cref="PlatformResponseKind"/>.
/// </summary>
public class HttpPlatformClientTests
{
    private static readonly IReadOnlyList<string> OneDocument = new[] { "{\"SourceReference\":\"REF-1\"}" };

    [Fact]
    public void Push_sends_api_key_and_contract_version_headers()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"Results\":[]}"));
        HttpPlatformClient client = CreateClient(handler);

        client.PushDocuments(OneDocument, Array.Empty<SourceTaxRegimeDto>());

        HttpRequestMessage request = handler.Requests[0];
        request.Headers.GetValues(AgentApiHeaders.AgentKey).Should().ContainSingle().Which.Should().Be("prefix.secret");
        request.Headers.GetValues(AgentApiHeaders.ContractVersion).Should().ContainSingle().Which.Should().Be(AgentContractVersion.ContractVersion);
    }

    [Fact]
    public void Push_body_carries_canonical_documents_and_source_tax_regimes()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"Results\":[]}"));
        HttpPlatformClient client = CreateClient(handler);

        client.PushDocuments(OneDocument, new[] { new SourceTaxRegimeDto("0", "Normal", 3) });

        string body = handler.RequestBodies[0];
        body.Should().Contain("\"Documents\":[{\"SourceReference\":\"REF-1\"}]");
        body.Should().Contain("\"SourceTaxRegimes\":");
        body.Should().Contain("\"0\"");
    }

    [Fact]
    public void Push_200_parses_per_document_results()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"Results\":[{\"SourceReference\":\"REF-1\",\"Status\":\"Accepted\"}]}"));
        HttpPlatformClient client = CreateClient(handler);

        PushBatchOutcome outcome = client.PushDocuments(OneDocument, Array.Empty<SourceTaxRegimeDto>());

        outcome.Kind.Should().Be(PlatformResponseKind.Ok);
        outcome.Results.Should().ContainSingle();
        outcome.Results[0].SourceReference.Should().Be("REF-1");
        outcome.Results[0].Status.Should().Be(DocumentPushStatus.Accepted);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PlatformResponseKind.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, PlatformResponseKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PlatformResponseKind.Unauthorized)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, PlatformResponseKind.PayloadTooLarge)]
    [InlineData((HttpStatusCode)426, PlatformResponseKind.UpgradeRequired)]
    [InlineData((HttpStatusCode)429, PlatformResponseKind.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PlatformResponseKind.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, PlatformResponseKind.Throttled)]
    public void Push_maps_http_status_to_response_kind(HttpStatusCode statusCode, PlatformResponseKind expected)
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Status(statusCode));
        HttpPlatformClient client = CreateClient(handler);

        PushBatchOutcome outcome = client.PushDocuments(OneDocument, Array.Empty<SourceTaxRegimeDto>());

        outcome.Kind.Should().Be(expected);
    }

    [Fact]
    public void Push_network_failure_maps_to_transport_error()
    {
        var handler = new StubHttpMessageHandler((req, body) => throw new HttpRequestException("réseau coupé"));
        HttpPlatformClient client = CreateClient(handler);

        PushBatchOutcome outcome = client.PushDocuments(OneDocument, Array.Empty<SourceTaxRegimeDto>());

        outcome.Kind.Should().Be(PlatformResponseKind.TransportError);
    }

    [Fact]
    public void Status_200_returns_reported_state()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"SourceReference\":\"REF-1\",\"PayloadHash\":\"h1\",\"Status\":\"Processed\"}"));
        HttpPlatformClient client = CreateClient(handler);

        DocumentStatusOutcome outcome = client.GetDocumentStatus("REF-1", "h1");

        outcome.Kind.Should().Be(PlatformResponseKind.Ok);
        outcome.Status.Should().Be(DocumentIntakeStatus.Processed);
    }

    [Fact]
    public void Status_404_is_unknown_not_terminal()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Status(HttpStatusCode.NotFound));
        HttpPlatformClient client = CreateClient(handler);

        DocumentStatusOutcome outcome = client.GetDocumentStatus("REF-1", "h1");

        outcome.Kind.Should().Be(PlatformResponseKind.Ok);
        outcome.Status.Should().BeNull();
    }

    [Fact]
    public void Status_query_carries_the_key_in_the_url()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Status(HttpStatusCode.NotFound));
        HttpPlatformClient client = CreateClient(handler);

        client.GetDocumentStatus("REF 1", "h1");

        // AbsoluteUri = forme ON-WIRE échappée (ToString() déséchapperait %20 en espace à l'affichage).
        string url = handler.Requests[0].RequestUri!.AbsoluteUri;
        url.Should().Contain("documents/status");
        url.Should().Contain("sourceReference=REF%201");
        url.Should().Contain("payloadHash=h1");
    }

    [Fact]
    public void Linked_pdf_push_succeeds_for_an_existing_file()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Status(HttpStatusCode.OK));
            HttpPlatformClient client = CreateClient(handler);

            PdfPushOutcome outcome = client.PushLinkedPdf("REF-1", path);

            outcome.Kind.Should().Be(PlatformResponseKind.Ok);
            handler.Requests[0].RequestUri!.ToString().Should().Contain("documents/REF-1/pdf");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pdf_push_of_a_missing_file_is_a_terminal_error()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Status(HttpStatusCode.OK));
        HttpPlatformClient client = CreateClient(handler);

        PdfPushOutcome outcome = client.PushPoolPdf("C:\\introuvable\\absent.pdf");

        outcome.Kind.Should().Be(PlatformResponseKind.BadRequest);
        handler.Requests.Should().BeEmpty();
    }

    private static HttpPlatformClient CreateClient(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://platform.test/") };
        return new HttpPlatformClient(http, "prefix.secret");
    }
}
