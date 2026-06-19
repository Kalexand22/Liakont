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

        // Tout le reste est false tant que la sandbox (PAS03) n'a rien confirmé (« incertain = false ») :
        caps.SupportsDomesticPaymentReporting.Should().BeFalse("flux 10.4 non documenté (O3)");
        caps.SupportsInternationalPaymentReporting.Should().BeFalse("flux 10.2 non documenté (O3)");
        caps.SupportsCreditNotes.Should().BeFalse("modèle d'avoir non confirmé (O7)");
        caps.SupportsTaxReportRetrieval.Should().BeFalse("endpoints tax reports non confirmés (O2)");
        caps.SupportsDocumentRetrieval.Should().BeFalse("endpoint de téléchargement non confirmé (O4)");
        caps.SupportsReportRectification.Should().BeFalse("flux RE non documenté (O9)");
        caps.SupportsB2bInvoicing.Should().BeFalse("phase 2");
        caps.SupportsSelfBilling.Should().BeFalse("émission 389 non confirmée en sandbox — déclaration honnête (MND07 / F15 §1.8)");
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
    public async Task Ungated_Tax_Report_Setting_Methods_Degrade_Gracefully_Without_Throwing()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "GOODS",
            EnterpriseSize = "PME",
        };

        // GetTaxReportSettingAsync et EnsureTaxReportSettingAsync NE sont gardées par AUCUNE capacité et sont
        // appelées par le chemin d'envoi : GetTaxReportSettingAsync par le diagnostic pré-envoi de
        // SendTenantJob (HORS SafeProcessAsync), EnsureTaxReportSettingAsync par l'action « Publier le SIREN ».
        // Elles ne doivent JAMAIS lever (invariant PAA01). Endpoint non confirmé (O2) → réglage VIDE/INACTIF
        // + écriture no-op : le SEND dégrade proprement en « SIREN non publié » (fail-closed) sans planter, et
        // le produit n'émet jamais vers un SIREN non publié (CLAUDE.md n°3).
        var setting = await client.GetTaxReportSettingAsync();
        setting.IsActiveOn(new DateOnly(2026, 6, 1)).Should().BeFalse(
            "un réglage vide bloque l'envoi (« SIREN non publié ») sans risquer un faux envoi");

        var ensure = async () => await client.EnsureTaxReportSettingAsync(request);
        await ensure.Should().NotThrowAsync("la publication ne bloque jamais le produit par une exception (PAA01)");

        handler.CallCount.Should().Be(0, "aucun endpoint réglage non confirmé n'est sondé en PAS02");
    }
}
