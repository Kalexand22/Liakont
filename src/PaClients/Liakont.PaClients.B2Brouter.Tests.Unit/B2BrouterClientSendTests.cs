namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests d'ENVOI de PAB01 : transformation pivot → JSON B2Brouter (facture 20 %, marge 2 lignes,
/// avoir) et classification de la réponse selon les 3 familles d'erreurs (F05 §4.1), avec handler HTTP
/// mocké. La forme « fil » est vérifiée sur le CORPS réellement posté (acceptance PAB01).
/// </summary>
public sealed class B2BrouterClientSendTests
{
    [Fact]
    public async Task SendDocument_Posts_To_The_Account_Invoices_Endpoint()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler, accountId: "ACC-42");

        await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        handler.CallCount.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequestUri!.AbsolutePath.Should().Be("/accounts/ACC-42/invoices.json");
    }

    [Fact]
    public async Task SendDocument_Invoice20_Transforms_To_Expected_Json()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);

        await client.SendDocumentAsync(B2BrouterTestData.Invoice20("F-2026-001"));

        var invoice = LastInvoice(handler);
        invoice.GetProperty("type").GetString().Should().Be("IssuedSimplifiedInvoice");
        invoice.GetProperty("number").GetString().Should().Be("F-2026-001");
        invoice.GetProperty("date").GetString().Should().Be("2026-01-15");
        invoice.GetProperty("currency").GetString().Should().Be("EUR");
        invoice.GetProperty("send_after_import").GetBoolean().Should().BeTrue();
        invoice.GetProperty("is_credit_note").GetBoolean().Should().BeFalse();
        invoice.TryGetProperty("amended_number", out _).Should().BeFalse("une facture normale n'émet pas amended_*");

        var lines = invoice.GetProperty("invoice_lines");
        lines.GetArrayLength().Should().Be(1);
        var line = lines[0];
        line.GetProperty("description").GetString().Should().Be("Prestation");
        line.GetProperty("price").GetDecimal().Should().Be(100m);
        line.GetProperty("tax").GetProperty("category").GetString().Should().Be("S");
        line.GetProperty("tax").GetProperty("percent").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task SendDocument_Margin_Produces_Two_Lines_With_Their_Categories()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);

        await client.SendDocumentAsync(B2BrouterTestData.MarginTwoLines());

        var lines = LastInvoice(handler).GetProperty("invoice_lines");
        lines.GetArrayLength().Should().Be(2, "modèle 2 lignes marge (F03 §2.3)");

        // Ligne adjudication : exonérée (E), 0 %, motif VATEX-EU-J — recopié du pivot, non inventé.
        lines[0].GetProperty("tax").GetProperty("category").GetString().Should().Be("E");
        lines[0].GetProperty("tax").GetProperty("percent").GetDecimal().Should().Be(0m);
        lines[0].GetProperty("tax").GetProperty("vatex").GetString().Should().Be("VATEX-EU-J");

        // Ligne frais acheteur : taux normal (S), 20 %, sans VATEX.
        lines[1].GetProperty("tax").GetProperty("category").GetString().Should().Be("S");
        lines[1].GetProperty("tax").GetProperty("percent").GetDecimal().Should().Be(20m);
        lines[1].GetProperty("tax").TryGetProperty("vatex", out _).Should().BeFalse("pas de VATEX sur une ligne taxée");
    }

    [Fact]
    public async Task SendDocument_CreditNote_Sets_Amend_Fields_From_Origin_Reference()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);

        await client.SendDocumentAsync(B2BrouterTestData.CreditNote("A-2026-001"));

        var invoice = LastInvoice(handler);
        invoice.GetProperty("is_credit_note").GetBoolean().Should().BeTrue();
        invoice.GetProperty("is_amend").GetBoolean().Should().BeTrue();
        invoice.GetProperty("amended_number").GetString().Should().Be("F-ORIGINE");
        invoice.GetProperty("amended_date").GetString().Should().Be("2026-01-10");

        // Le signe n'est PAS inversé : l'avoir est positif + is_credit_note (F07-F08) — pas de
        // négatif combiné aux flags d'amendement (qui inverserait deux fois).
        invoice.GetProperty("invoice_lines")[0].GetProperty("price").GetDecimal().Should().Be(50m);
    }

    [Fact]
    public async Task SendDocument_Without_SendAfterImport_Emits_The_Flag_False_And_Is_Not_Issued()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, """{"id":"INV-2","state":"new"}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20(), sendAfterImport: false);

        LastInvoice(handler).GetProperty("send_after_import").GetBoolean().Should().BeFalse();
        result.State.Should().Be(
            PaSendState.New,
            "un document créé sans envoi (état « new ») n'est PAS émis — non facturable (F05 §2)");
        result.PaDocumentId.Should().Be("INV-2");
    }

    [Fact]
    public async Task SendDocument_Sending_State_Is_Not_Reported_As_Issued()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, """{"id":"INV-9","state":"sending"}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.Sending,
            "un envoi encore « sending » n'est pas confirmé émis (correction fiscale/audit)");
    }

    [Fact]
    public async Task SendDocument_Emits_Line_Net_As_Quantity_One_To_Avoid_Double_Counting()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);
        var doc = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-QTY",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-F-QTY",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(300m, 60m, 360m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: [new PivotLineDto("Lot de 3", 300m, quantity: 3m, taxes: [new PivotLineTaxDto(60m, 20m, VatCategory.S)])]);

        await client.SendDocumentAsync(doc);

        var line = LastInvoice(handler).GetProperty("invoice_lines")[0];
        line.GetProperty("quantity").GetDecimal().Should().Be(1m, "le net de ligne est émis en quantité 1");
        line.GetProperty("price").GetDecimal().Should().Be(
            300m,
            "le total ligne = NetAmount (pas de double comptage price × quantité)");
    }

    [Fact]
    public async Task SendDocument_Line_With_Multiple_Tax_Breakdowns_Is_Blocked_Not_Silently_Dropped()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);
        var doc = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-MULTITAX",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-F-MULTITAX",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines:
            [
                new PivotLineDto(
                    "Ligne à 2 ventilations",
                    100m,
                    taxes: [new PivotLineTaxDto(15m, 20m, VatCategory.S), new PivotLineTaxDto(5m, 10m, VatCategory.AA)]),
            ]);

        var act = () => client.SendDocumentAsync(doc);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "une ligne à plusieurs ventilations TVA (BG-30) est bloquée, jamais dropée en silence (CLAUDE.md n°3)");
        handler.CallCount.Should().Be(0, "rien n'est envoyé à la PA quand le document est mal formé");
    }

    [Fact]
    public async Task SendDocument_Issued_Maps_To_Issued_With_Id_TaxReports_And_Raw()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("INV-1001");
        result.TaxReportIds.Should().ContainSingle().Which.Should().Be("TR-1");
        result.RawResponse.Should().NotBeNullOrEmpty("la réponse brute est conservée pour l'audit (F06/DR6)");
    }

    [Fact]
    public async Task SendDocument_Silent_Error_200_With_Errors_Is_Rejected_Not_Issued()
    {
        // Cas piégeux F05 §4.1 : HTTP 200 mais errors[] non vide (VATEX manquant) → rejet, jamais émis.
        const string body = """{"id":"INV-3","state":"issued","errors":[{"code":"VATEX_MISSING","message":"VATEX requis"}]}""";
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, body);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.RejectedByPa,
            "une erreur silencieuse (200 + errors[]) est un rejet, pas une émission (F05 §4.1)");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("VATEX_MISSING");
        result.RawResponse.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendDocument_4xx_With_Errors_Is_Rejected_Without_Retry()
    {
        const string body = """{"errors":[{"code":"INVALID","message":"Payload invalide"}]}""";
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.UnprocessableEntity, body);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().NotBeEmpty();
        handler.CallCount.Should().Be(1, "un 4xx ne se retente pas (F05 §4.1)");
    }

    [Fact]
    public async Task SendDocument_4xx_Without_Body_Is_Rejected_With_Status_Detail()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.Unauthorized, string.Empty);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("401");
    }

    [Fact]
    public async Task SendDocument_5xx_Is_Technical_Error_Retryable()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.ServiceUnavailable, "oups");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "5xx est re-tentable au prochain run (F05 §4.1)");
        result.RawResponse.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendDocument_Network_Error_Is_Technical_Error()
    {
        var handler = StubHttpMessageHandler.Throws(new HttpRequestException("connexion refusée"));
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_NETWORK");
    }

    [Fact]
    public async Task SendDocument_Timeout_Is_Technical_Error()
    {
        var handler = StubHttpMessageHandler.Throws(new TaskCanceledException("délai dépassé"));
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_TIMEOUT");
    }

    [Fact]
    public async Task SendDocument_Caller_Cancellation_Propagates_Without_Calling_The_Pa()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => client.SendDocumentAsync(B2BrouterTestData.Invoice20(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("l'annulation appelant n'est pas une erreur technique");
        handler.CallCount.Should().Be(0);
    }

    private static JsonElement LastInvoice(StubHttpMessageHandler handler)
    {
        handler.LastRequestBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        return doc.RootElement.GetProperty("invoice").Clone();
    }
}
