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
        caps.MaxDocumentsPerRequest.Should().BeNull();
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
    public async Task Unconfirmed_Tax_Report_Reads_Throw_A_Traceable_Exception_Instead_Of_Faking_Data()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var client = SuperPdpTestData.CreateClient(handler);
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "GOODS",
            EnterpriseSize = "PME",
        };

        // Lectures dont l'endpoint n'est pas confirmé en sandbox (SupportsTaxReportRetrieval = false) :
        // lèvent plutôt que de renvoyer une liste vide ou un réglage faux (mensonge fiscal — CLAUDE.md n°3).
        await ((Func<Task>)(() => client.ListTaxReportsAsync())).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetTaxReportAsync("TR-1"))).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetAccountInfoAsync())).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetTaxReportSettingAsync())).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.EnsureTaxReportSettingAsync(request))).Should().ThrowAsync<NotImplementedException>();
        handler.CallCount.Should().Be(0, "aucune lecture non confirmée ne touche le réseau");
    }
}
