namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests d'ÉMISSION de PAS02 sur le contrat RÉEL (✅ OpenAPI + sandbox 2026-06-12, F14 §3.2/§4.1) :
/// pivot → JSON <c>en16931</c> (vérifié sur le corps de la CONVERSION) → XML CII transmis à l'émission
/// (<c>?external_id=</c>) → classification par les <c>events[]</c>. Tout est piloté par un handler
/// mocké — aucune PA réelle n'est appelée. Le jeton bearer est injecté par le fournisseur de jeton de test.
/// </summary>
public sealed class SuperPdpClientSendTests
{
    [Fact]
    public async Task Send_Converts_Then_Posts_The_Xml_With_Bearer_And_External_Id()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20("F-2026-042"));

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("1001");

        handler.Requests.Should().HaveCount(2, "le chemin réel est conversion PUIS émission (F14 §3.2)");
        var convert = handler.Requests[0];
        convert.Path.Should().Be("/v1.beta/invoices/convert");
        convert.Uri!.Query.Should().Contain("from=en16931").And.Contain("to=cii");
        convert.Authorization!.Scheme.Should().Be("Bearer");
        convert.Authorization.Parameter.Should().Be(StubTokenProvider.NominalToken);

        var post = handler.Requests[1];
        post.Path.Should().Be("/v1.beta/invoices");
        post.Uri!.Query.Should().Contain(
            "external_id=F-2026-042", "l'external_id porte le numéro de document (clé d'idempotence, F14 §4.1)");
        post.Body.Should().Be(SuperPdpTestData.CiiXml, "le XML rendu par la conversion est transmis TEL QUEL");
        post.Authorization!.Parameter.Should().Be(StubTokenProvider.NominalToken);
    }

    [Fact]
    public async Task Send_Serializes_The_Pivot_To_The_En16931_Json()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.Invoice20("F-2026-042"));

        var invoice = ConvertBody(handler);
        invoice.GetProperty("number").GetString().Should().Be("F-2026-042");
        invoice.GetProperty("issue_date").GetString().Should().Be("2026-01-15");
        invoice.GetProperty("type_code").GetInt32().Should().Be(
            380, "le type_code est un NOMBRE JSON (l'API rejette une chaîne — F14 §3.2)");
        invoice.GetProperty("currency_code").GetString().Should().Be("EUR");
        invoice.GetProperty("process_control").GetProperty("specification_identifier").GetString()
            .Should().Be("urn:cen.eu:en16931:2017");

        var seller = invoice.GetProperty("seller");
        seller.GetProperty("name").GetString().Should().Be("SVV Démo");
        seller.GetProperty("legal_registration_identifier").GetProperty("value").GetString().Should().Be("123456789");
        seller.GetProperty("legal_registration_identifier").GetProperty("scheme").GetString().Should().Be("0002");
        seller.GetProperty("vat_identifier").GetString().Should().Be("FR32123456789");
        seller.GetProperty("electronic_address").GetProperty("value").GetString()
            .Should().Be("123456789", "l'adressage d'annuaire passe par le SIREN scheme 0002 (✅ sandbox, F14 §3.2)");

        var buyer = invoice.GetProperty("buyer");
        buyer.GetProperty("name").GetString().Should().Be("Client Démo");
        buyer.GetProperty("electronic_address").GetProperty("value").GetString().Should().Be("987654321");

        var totals = invoice.GetProperty("totals");
        totals.GetProperty("total_without_vat").GetDecimal().Should().Be(100m);
        totals.GetProperty("total_vat_amount").GetProperty("value").GetDecimal().Should().Be(20m);
        totals.GetProperty("total_with_vat").GetDecimal().Should().Be(120m);
        totals.GetProperty("amount_due_for_payment").GetDecimal().Should().Be(120m);

        var breakdown = invoice.GetProperty("vat_break_down");
        breakdown.GetArrayLength().Should().Be(1);
        breakdown[0].GetProperty("vat_category_code").GetString().Should().Be("S");
        breakdown[0].GetProperty("vat_category_rate").GetDecimal().Should().Be(20m);
        breakdown[0].GetProperty("vat_category_taxable_amount").GetDecimal().Should().Be(100m);
        breakdown[0].GetProperty("vat_category_tax_amount").GetDecimal().Should().Be(20m);

        var lines = invoice.GetProperty("lines");
        lines.GetArrayLength().Should().Be(1);
        lines[0].GetProperty("net_amount").GetDecimal().Should().Be(100m);
        lines[0].GetProperty("invoiced_quantity_code").GetString().Should().Be("C62");
        lines[0].GetProperty("vat_information").GetProperty("invoiced_item_vat_category_code").GetString().Should().Be("S");
        lines[0].GetProperty("item_information").GetProperty("name").GetString().Should().Be("Prestation");
    }

    [Fact]
    public async Task Send_With_PaymentDueDate_Emits_payment_due_date()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.Invoice20WithDueDate("F-ECHEANCE", new DateTime(2026, 2, 15)));

        // EXT01 : une facture à échéance NON SOLDÉE porte BT-9 → le builder émet payment_due_date
        // (yyyy-MM-dd), ce qui satisfait BR-CO-25 sur un montant dû positif (F14 §3.2/O11).
        var invoice = ConvertBody(handler);
        invoice.GetProperty("payment_due_date").GetString().Should().Be("2026-02-15");
    }

    [Fact]
    public async Task Send_With_UnitCode_Keeps_neutral_C62_on_synthetic_quantity_one_line()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.Invoice20WithUnitCode("F-UNITE", "KGM"));

        // RD407 (BT-130) : la ligne SuperPDP est un agrégat synthétique émis en quantité 1 → son unité reste
        // l'unité neutre C62, même quand le pivot porte une unité. Projeter « 1 KGM » au prix du total serait
        // incohérent (CLAUDE.md n°3) : l'émission fidèle de BT-130 côté SuperPDP est un raffinement différé (RD407).
        // FacturX, qui émet la quantité réelle, projette l'unité — cf. CrossIndustryInvoiceSerializerTests.
        var invoice = ConvertBody(handler);
        invoice.GetProperty("lines")[0].GetProperty("invoiced_quantity_code").GetString()
            .Should().Be("C62", "l'agrégat quantité=1 garde l'unité neutre ; aucune unité réelle projetée sur une quantité forcée");
    }

    [Fact]
    public async Task Send_Without_PaymentDueDate_Omits_payment_due_date()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        // Sans échéance dans le pivot, le champ est OMIS (WhenWritingNull) : le comportement BR-CO-25 du
        // converter (rejet d'un montant dû positif) reste INCHANGÉ — aucune échéance fabriquée (CLAUDE.md n°2).
        var invoice = ConvertBody(handler);
        invoice.TryGetProperty("payment_due_date", out _).Should().BeFalse(
            "un pivot sans échéance n'émet aucun payment_due_date (comportement BR-CO-25 conservé)");
    }

    [Fact]
    public async Task Send_Without_SendAfterImport_Is_Rejected_Locally_Without_Any_Call()
    {
        var handler = new RoutedHttpMessageHandler();
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20(), sendAfterImport: false);

        // Super PDP n'expose pas de « création sans envoi » (✅ OpenAPI — F14 §3.2) : émettre quand même
        // serait une émission fiscale NON VOULUE → résultat typé, AUCUN appel (CLAUDE.md n°3).
        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("SPDP_NO_DRAFT");
        handler.Requests.Should().BeEmpty("la garde locale rejette AVANT tout appel PA");
    }

    [Fact]
    public async Task Send_Without_Customer_Siren_Is_Rejected_Locally_Without_Any_Call()
    {
        var handler = new RoutedHttpMessageHandler();
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20WithoutCustomer());

        // POST /invoices exige un destinataire ADRESSABLE (« missing buyer electronic address », ✅
        // constaté sandbox — F14 §3.2) : sans SIREN acheteur, rejet local actionnable, pas d'envoi voué à l'échec.
        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("SPDP_BUYER_NOT_ADDRESSABLE");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_Without_Supplier_Siren_Is_Rejected_Locally_Without_Any_Call()
    {
        var handler = new RoutedHttpMessageHandler();
        var client = SuperPdpTestData.CreateClient(handler);
        var doc = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-NOSIREN",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-F-NOSIREN",
            supplier: new PivotPartyDto("SVV Démo"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines: [new PivotLineDto("Prestation", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])]);

        var result = await client.SendDocumentAsync(doc);

        // La PA vérifie que le vendeur correspond à l'entreprise du compte (✅ constaté sandbox — F14
        // §3.2) : sans SIREN vendeur, ni BT-30 ni l'adressage ne sont constructibles → rejet local clair.
        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("SPDP_SELLER_SIREN_MISSING");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_Margin_TwoLines_Groups_The_Vat_Break_Down_And_Keeps_The_Vatex_Code()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        await client.SendDocumentAsync(SuperPdpTestData.MarginTwoLines());

        var invoice = ConvertBody(handler);
        var lines = invoice.GetProperty("lines");
        lines.GetArrayLength().Should().Be(2);
        lines[0].GetProperty("vat_information").GetProperty("invoiced_item_vat_category_code").GetString().Should().Be("E");
        lines[1].GetProperty("vat_information").GetProperty("invoiced_item_vat_category_code").GetString().Should().Be("S");

        // BG-23 : un groupe par (catégorie, taux, VATEX), sommes recopiées — le VATEX de la ligne E
        // remonte au niveau document (EN 16931 BT-121), jamais inventé (F03 §2.2).
        var breakdown = invoice.GetProperty("vat_break_down");
        breakdown.GetArrayLength().Should().Be(2);

        var margin = FindBreakdown(breakdown, "E");
        margin.GetProperty("vat_exemption_reason_code").GetString().Should().Be("VATEX-EU-J");
        margin.GetProperty("vat_category_taxable_amount").GetDecimal().Should().Be(1000m);
        margin.GetProperty("vat_category_tax_amount").GetDecimal().Should().Be(0m);

        var fees = FindBreakdown(breakdown, "S");
        fees.GetProperty("vat_category_rate").GetDecimal().Should().Be(20m);
        fees.GetProperty("vat_category_taxable_amount").GetDecimal().Should().Be(200m);
        fees.GetProperty("vat_category_tax_amount").GetDecimal().Should().Be(40m);
    }

    [Fact]
    public async Task Send_Uploaded_Only_Is_Sending_Never_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.UploadedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        // L'envoi est ASYNCHRONE : 200 + api:uploadé = « téléversée », l'émission viendra du polling —
        // jamais « émis » sur le seul code HTTP (F14 §4.1, CLAUDE.md n°3).
        result.State.Should().Be(PaSendState.Sending);
        result.PaDocumentId.Should().Be("1002");
    }

    [Fact]
    public async Task Send_Issued_Result_Preserves_The_Raw_Response_For_Audit()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.RawResponse.Should().Contain("fr:201", "la réponse brute est conservée pour l'audit (F06/DR6)");
    }

    [Fact]
    public async Task Send_Rejection_4xx_Surfaces_The_Message_Intact_And_Is_Not_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(
                HttpStatusCode.BadRequest,
                SuperPdpTestData.ErrorJson(400, "L'entreprise (000000002) liée à cette session ne correspond pas au vendeur de la facture (123456789)."))
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.PaDocumentId.Should().BeNull("un rejet n'émet rien");
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("ne correspond pas au vendeur", "le message Super PDP remonte INTACT (F14 §4.1)");
        handler.ListCount.Should().Be(
            1, "un rejet sans identifiant vérifie d'abord le refus anti-doublon (raccrochage — F14 §4.1)");
        handler.PostCount.Should().Be(1, "jamais de ré-émission");
    }

    [Fact]
    public async Task Send_Rejected_As_Duplicate_Reattaches_The_Existing_Invoice()
    {
        const string number = "F-DUP";
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .OnPost(HttpStatusCode.BadRequest, SuperPdpTestData.ErrorJson(400, "La facture est déjà existante (id 2001)"))
            .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.InvoiceListJsonWith(number));
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20(number));

        // Le serveur REFUSE de recréer une facture au même numéro (anti-doublon, ✅ constaté sandbox
        // 2026-06-12) : le rejet est en réalité « déjà créée » → on raccroche son état RÉEL, jamais un
        // faux état « rejeté » sur un document émis (CLAUDE.md n°3).
        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("2001");
        handler.PostCount.Should().Be(1, "jamais de ré-émission : raccrochage par external_id");
    }

    [Fact]
    public async Task Send_Convert_Rejection_Surfaces_The_BR_Rule_And_Never_Posts()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnConvert(
                HttpStatusCode.BadRequest,
                SuperPdpTestData.ErrorJson(400, "[BR-S-02]-An Invoice that contains an Invoice line..."));
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        // Le converter applique les règles EN 16931 officielles : son rejet est terminal, le message
        // BR-* remonte intact et RIEN n'est émis (F14 §3.2/§4.1).
        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle().Which.Message.Should().StartWith("[BR-S-02]");
        handler.PostCount.Should().Be(0, "aucune émission après un échec de conversion");
        handler.ListCount.Should().Be(0, "la conversion ne crée rien : aucun raccrochage à tenter");
    }

    [Fact]
    public async Task Send_With_Multiple_Tax_Breakdowns_On_A_Line_Is_Blocked_Never_Truncated()
    {
        var handler = new RoutedHttpMessageHandler();
        var client = SuperPdpTestData.CreateClient(handler);

        var doc = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-BG30",
            issueDate: new DateTime(2026, 1, 1),
            sourceReference: "SRC-BG30",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines:
            [
                new PivotLineDto(
                    "Ligne à deux ventilations",
                    100m,
                    taxes:
                    [
                        new PivotLineTaxDto(10m, 10m, VatCategory.S),
                        new PivotLineTaxDto(10m, 20m, VatCategory.S),
                    ]),
            ]);

        var act = async () => await client.SendDocumentAsync(doc);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "une ligne multi-ventilations (EN 16931 BG-30) est bloquée, jamais tronquée en silence (CLAUDE.md n°3)");
        handler.Requests.Should().BeEmpty("le payload mal formé lève AVANT le premier appel PA");
    }

    [Fact]
    public async Task Send_With_A_Line_Without_Tax_Is_Blocked_Never_Guessed()
    {
        var handler = new RoutedHttpMessageHandler();
        var client = SuperPdpTestData.CreateClient(handler);

        var doc = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-NOTAX",
            issueDate: new DateTime(2026, 1, 1),
            sourceReference: "SRC-NOTAX",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 0m, 100m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines: [new PivotLineDto("Ligne sans ventilation", 100m)]);

        var act = async () => await client.SendDocumentAsync(doc);

        // Le schéma en_invoice EXIGE la catégorie de TVA par ligne : sans ventilation posée par la
        // plateforme (F03), on bloque — aucune catégorie n'est inventée (CLAUDE.md n°2/3).
        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    // Extrait le JSON en_invoice envoyé à la CONVERSION (le payload « fil » construit par le builder).
    private static JsonElement ConvertBody(RoutedHttpMessageHandler handler)
    {
        var convert = handler.Requests.First(r => r.Path.EndsWith("/invoices/convert", StringComparison.Ordinal));
        convert.Body.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(convert.Body!);
        return document.RootElement.Clone();
    }

    private static JsonElement FindBreakdown(JsonElement breakdown, string categoryCode)
    {
        foreach (var entry in breakdown.EnumerateArray())
        {
            if (entry.GetProperty("vat_category_code").GetString() == categoryCode)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"Aucune entrée vat_break_down pour la catégorie {categoryCode}.");
    }
}
