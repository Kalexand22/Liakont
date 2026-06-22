namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests de l'invariant produit (PAA01) appliqué à Super PDP : une capacité non déclarée dégrade en
/// résultat TYPÉ et journalisable, JAMAIS une exception ni un blocage du produit. Couvre la cohérence des
/// capacités PROVISOIRES de PAS02 (F14 §5 : B2C seul vérifié, tout le reste false) et le fait que les
/// lectures non confirmées en sandbox lèvent une exception traçable plutôt que de renvoyer une donnée
/// fiscale fausse (CLAUDE.md n°3).
/// </summary>
public sealed class SuperPdpClientCapabilityTests
{
    [Fact]
    public void Declared_Capabilities_Match_The_Provisional_PAS02_State()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var caps = SuperPdpTestData.CreateClient(handler).Capabilities;

        caps.PaName.Should().Be("Super PDP");
        caps.SupportsB2cReporting.Should().BeTrue("le B2C est ✅ vérifié (DR17) — seule capacité true en PAS02 (F14 §5)");

        // Facturation B2B : vérifiée en sandbox (envoi réel facture 72272) — activée sur directive de recette (18/06/2026).
        caps.SupportsB2bInvoicing.Should().BeTrue("facturation B2B vérifiée en sandbox — envoi réel facture 72272");

        // Les flux non confirmés restent false tant que la sandbox (PAS03) n'a rien validé (« incertain = false ») :
        caps.SupportsDomesticPaymentReporting.Should().BeFalse("flux 10.4 non documenté (O3)");
        caps.SupportsInternationalPaymentReporting.Should().BeFalse("flux 10.2 non documenté (O3)");
        caps.SupportsCreditNotes.Should().BeFalse("modèle d'avoir non confirmé (O7)");
        caps.SupportsTaxReportRetrieval.Should().BeFalse("endpoints tax reports non confirmés (O2)");
        caps.SupportsDocumentRetrieval.Should().BeFalse("endpoint de téléchargement non confirmé (O4)");
        caps.SupportsReportRectification.Should().BeFalse("flux RE non documenté (O9)");
        caps.SupportsSelfBilling.Should().BeFalse("émission 389 non confirmée en sandbox — déclaration honnête (MND07 / F15 §1.8)");
        caps.SupportsMarginAmountReporting.Should().BeTrue("montant de marge TMA1 ✅ POST confirmé sandbox 2026-06-22 (id 585) + forme ancrée F03 §2.5 (B2C09c)");
        caps.MaxDocumentsPerRequest.Should().BeNull();
    }

    [Fact]
    public async Task SelfBilled389_Without_Capability_Degrades_To_Typed_Result_And_Does_Not_Call_The_Pa()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler); // PAS02 : SupportsSelfBilling = false

        var result = await client.SendDocumentAsync(
            SuperPdpTestData.Invoice20("F-389"),
            projection: PaOutboundProjection.ForSelfBilled("ARM-A-1"));

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.SelfBilling);
        handler.CallCount.Should().Be(0, "un 389 non supporté ne part jamais sur le réseau, jamais dégradé en facture 380");
    }

    [Theory]
    [InlineData(PaymentReportFlux.Domestic, PaCapability.DomesticPaymentReporting)]
    [InlineData(PaymentReportFlux.International, PaCapability.InternationalPaymentReporting)]
    public async Task SendPaymentReport_Is_A_Typed_Capability_Gap(PaymentReportFlux flux, PaCapability expected)
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);
        var period = new PaymentReportPeriod
        {
            Flux = flux,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await client.SendPaymentReportAsync(period);

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(expected);
        result.CapabilityNotSupported.OperatorMessage.Should().NotBeNullOrWhiteSpace(
            "le message opérateur français est journalisable (CLAUDE.md n°12)");
        handler.CallCount.Should().Be(0, "aucune capacité → aucun appel réseau");
    }

    [Fact]
    public async Task GetGeneratedDocument_Is_A_Typed_Capability_Gap()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.GetGeneratedDocumentAsync("INV-1001");

        result.Content.Should().BeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendCreditNote_Without_Capability_Degrades_To_Typed_Result_And_Does_Not_Call_The_Pa()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(SuperPdpTestData.CreditNote());

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.CreditNotes);
        handler.CallCount.Should().Be(0, "un avoir non supporté ne part jamais sur le réseau");
    }

    [Fact]
    public async Task Capability_Gated_Tax_Report_Reads_Throw_A_Traceable_Exception_Instead_Of_Faking_Data()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        // Lectures GARDÉES par SupportsTaxReportRetrieval = false (appelées UNIQUEMENT sous cette capacité,
        // SyncTenantJob) : lèvent plutôt que de renvoyer une liste vide = « aucun tax report » (mensonge
        // fiscal, sous-déclaration — CLAUDE.md n°3). PAS03 les confirme en sandbox PUIS bascule la capacité.
        await ((Func<Task>)(() => client.ListTaxReportsAsync())).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetTaxReportAsync("TR-1"))).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetAccountInfoAsync())).Should().ThrowAsync<NotImplementedException>();
        handler.CallCount.Should().Be(0, "aucune lecture non confirmée ne touche le réseau");
    }

    [Fact]
    public async Task Tax_Report_Setting_Is_Active_When_Companies_Me_Returns_A_Registered_Siren()
    {
        // GetTaxReportSettingAsync / EnsureTaxReportSettingAsync NE sont gardées par AUCUNE capacité (chemin
        // d'envoi : diagnostic pré-envoi de SendTenantJob, action « Publier le SIREN »). Super PDP n'expose pas
        // de tax_report_setting éditable : l'état réel est LU via GET /v1.beta/companies/me (✅ sandbox
        // 2026-06-12). Entreprise présente avec un SIREN (champ « number ») → transmission ACTIVE (StartDate =
        // aujourd'hui, IsActiveOn true) ; EnsureTaxReportSettingAsync est un no-op idempotent (ne lève pas).
        const string companyJson = """{"number":"000000002","formal_name":"Burger Queen"}""";
        var handler = new PathRoutingHttpMessageHandler()
            .On(SuperPdpDefaults.CompaniesMePath, HttpStatusCode.OK, companyJson);
        var client = SuperPdpTestData.CreateClient(handler);
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "GOODS",
            EnterpriseSize = "PME",
        };

        var setting = await client.GetTaxReportSettingAsync();
        setting.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeTrue(
            "une entreprise enregistrée côté Super PDP (SIREN « number ») rend la transmission active");
        setting.RawResponse.Should().Contain("000000002", "la réponse companies/me est conservée pour l'audit (F06/DR6)");

        var ensure = async () => await client.EnsureTaxReportSettingAsync(request);
        await ensure.Should().NotThrowAsync(
            "une entreprise déjà vérifiée → no-op idempotent, jamais une exception (PAA01)");

        // Au moins une requête a touché companies/me (GET pour chacune des deux méthodes) — endpoint réel.
        handler.Requests.Should().OnlyContain(r => r.Path.EndsWith(SuperPdpDefaults.CompaniesMePath, StringComparison.Ordinal));
        handler.Requests.Should().HaveCount(2, "chaque méthode LIT l'état réel via GET companies/me");
    }

    [Fact]
    public async Task Tax_Report_Setting_Is_Inactive_When_Companies_Me_Has_No_Siren_And_Ensure_Throws_Actionable()
    {
        // Aucune entreprise vérifiée (companies/me sans « number ») : la transmission est INACTIVE (réglage vide
        // → IsActiveOn false, le SEND dégrade en « SIREN non publié », fail-closed, jamais un faux envoi —
        // CLAUDE.md n°3) ET l'action « Publier le SIREN » lève un message opérateur actionnable (FR, CLAUDE.md
        // n°12) — la KYC se fait dans l'espace Super PDP, pas depuis Liakont.
        const string companyWithoutSiren = """{"formal_name":"Burger Queen"}""";
        var handler = new PathRoutingHttpMessageHandler()
            .On(SuperPdpDefaults.CompaniesMePath, HttpStatusCode.OK, companyWithoutSiren);
        var client = SuperPdpTestData.CreateClient(handler);
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "GOODS",
            EnterpriseSize = "PME",
        };

        var setting = await client.GetTaxReportSettingAsync();
        setting.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeFalse(
            "sans SIREN vérifié, l'envoi reste bloqué « SIREN non publié » (fail-closed)");

        var ensure = async () => await client.EnsureTaxReportSettingAsync(request);
        (await ensure.Should().ThrowAsync<HttpRequestException>(
            "la publication / vérification du SIREN (KYC) se fait dans l'espace Super PDP"))
            .Which.Message.Should().Contain("espace Super PDP");
    }

    [Fact]
    public async Task Tax_Report_Setting_Is_Inactive_When_Companies_Me_Returns_404()
    {
        // Indisponibilité / 404 sur companies/me : GetTaxReportSettingAsync NE DOIT JAMAIS lever (PAA01 —
        // elle est appelée HORS SafeProcessAsync par le diagnostic pré-envoi) ; elle dégrade en réglage INACTIF
        // (fail-closed, re-tentable au cycle suivant). EnsureTaxReportSettingAsync lève le message actionnable.
        var handler = new PathRoutingHttpMessageHandler()
            .On(SuperPdpDefaults.CompaniesMePath, HttpStatusCode.NotFound, """{"http_status_code":404,"message":"Not found"}""");
        var client = SuperPdpTestData.CreateClient(handler);
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "GOODS",
            EnterpriseSize = "PME",
        };

        var settingTask = async () => await client.GetTaxReportSettingAsync();
        var setting = (await settingTask.Should().NotThrowAsync(
            "le diagnostic pré-envoi ne plante jamais le job sur une indisponibilité (PAA01)")).Subject;
        setting.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeFalse("404 → réglage inactif (fail-closed)");

        var ensure = async () => await client.EnsureTaxReportSettingAsync(request);
        await ensure.Should().ThrowAsync<HttpRequestException>("aucune entreprise vérifiée → action opérateur requise");
    }
}
