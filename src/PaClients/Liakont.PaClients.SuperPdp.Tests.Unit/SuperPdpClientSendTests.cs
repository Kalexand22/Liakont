namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests d'ÉMISSION B2C de PAS02 : transformation pivot → payload « fil » Super PDP (vérifié sur le corps
/// HTTP du mock) + classification de la réponse (F14 §3.2/§4.1). Tout est piloté par un handler mocké —
/// aucune PA réelle n'est appelée. Le jeton bearer est injecté par le fournisseur de jeton de test.
/// </summary>
public sealed class SuperPdpClientSendTests
{
    [Fact]
    public async Task Send_Posts_To_The_Versioned_Invoices_Endpoint_With_Bearer_Token()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("INV-1001");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/v1.beta/invoices");
        handler.LastAuthorization.Should().NotBeNull("le jeton OAuth bearer doit être injecté (F14 §3.1)");
        handler.LastAuthorization!.Scheme.Should().Be("Bearer");
        handler.LastAuthorization.Parameter.Should().Be(StubTokenProvider.NominalToken);
    }

    [Fact]
    public async Task Send_Serializes_The_Pivot_To_The_SuperPdp_Invoice_Envelope()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.Invoice20("F-2026-042"), sendAfterImport: true);

        var invoice = InvoiceElement(handler);
        invoice.GetProperty("number").GetString().Should().Be("F-2026-042");
        invoice.GetProperty("date").GetString().Should().Be("2026-01-15");
        invoice.GetProperty("currency").GetString().Should().Be("EUR");
        invoice.GetProperty("send_after_import").GetBoolean().Should().BeTrue();
        invoice.GetProperty("invoice_lines").GetArrayLength().Should().Be(1);

        var tax = invoice.GetProperty("invoice_lines")[0].GetProperty("tax");
        tax.GetProperty("category").GetString().Should().Be("S");
        tax.GetProperty("percent").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Send_Without_SendAfterImport_Sets_The_Flag_False()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, """{"id":"INV-9","state":"new"}""");
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20(), sendAfterImport: false);

        InvoiceElement(handler).GetProperty("send_after_import").GetBoolean().Should().BeFalse();

        // État « new » = créé sans envoi, NON facturable : surtout pas « émis » (F14 §4.1, CLAUDE.md n°3).
        result.State.Should().Be(PaSendState.New);
    }

    [Fact]
    public async Task Send_Margin_TwoLines_Maps_Each_Category_And_The_Vatex_Code()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.MarginTwoLines());

        var lines = InvoiceElement(handler).GetProperty("invoice_lines");
        lines.GetArrayLength().Should().Be(2);

        var margin = lines[0].GetProperty("tax");
        margin.GetProperty("category").GetString().Should().Be("E");
        margin.GetProperty("vatex").GetString().Should().Be("VATEX-EU-J");

        var fees = lines[1].GetProperty("tax");
        fees.GetProperty("category").GetString().Should().Be("S");
        fees.GetProperty("percent").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Send_Issued_Result_Preserves_The_Raw_Response_For_Audit()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.RawResponse.Should().Contain("INV-1001", "la réponse brute est conservée pour l'audit (F06/DR6)");
        result.TaxReportIds.Should().ContainSingle().Which.Should().Be("TR-1");
    }

    [Fact]
    public async Task Send_Rejection_4xx_Surfaces_Errors_Intact_And_Is_Not_Issued()
    {
        const string body = """{"errors":[{"code":"INVALID_SIREN","message":"SIREN inconnu de l'annuaire."}]}""";
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.UnprocessableEntity, body);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.PaDocumentId.Should().BeNull("un rejet n'émet rien");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("INVALID_SIREN");
    }

    [Fact]
    public async Task Send_With_Multiple_Tax_Breakdowns_On_A_Line_Is_Blocked_Never_Truncated()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var doc = new Agent.Contracts.Pivot.PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-BG30",
            issueDate: new DateTime(2026, 1, 1),
            sourceReference: "SRC-BG30",
            supplier: new Agent.Contracts.Pivot.PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new Agent.Contracts.Pivot.PivotTotalsDto(100m, 20m, 120m),
            operationCategory: Agent.Contracts.Pivot.OperationCategory.LivraisonBiens,
            lines:
            [
                new Agent.Contracts.Pivot.PivotLineDto(
                    "Ligne à deux ventilations",
                    100m,
                    taxes:
                    [
                        new Agent.Contracts.Pivot.PivotLineTaxDto(10m, 10m, Agent.Contracts.Pivot.VatCategory.S),
                        new Agent.Contracts.Pivot.PivotLineTaxDto(10m, 20m, Agent.Contracts.Pivot.VatCategory.S),
                    ]),
            ]);

        var act = async () => await client.SendDocumentAsync(doc);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "une ligne multi-ventilations (EN 16931 BG-30) est bloquée, jamais tronquée en silence (CLAUDE.md n°3)");
        handler.CallCount.Should().Be(0, "le payload mal formé lève AVANT le premier appel PA");
    }

    // Extrait l'objet « invoice » de l'enveloppe POST capturée par le mock.
    private static JsonElement InvoiceElement(StubHttpMessageHandler handler)
    {
        handler.LastRequestBody.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        return document.RootElement.GetProperty("invoice").Clone();
    }
}
