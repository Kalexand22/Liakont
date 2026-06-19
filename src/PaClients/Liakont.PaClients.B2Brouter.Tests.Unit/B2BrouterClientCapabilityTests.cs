namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests de l'invariant produit (PAA01) appliqué à B2Brouter : une capacité non déclarée dégrade en
/// résultat TYPÉ et journalisable, JAMAIS une exception ni un blocage du produit. Couvre aussi la
/// cohérence des capacités déclarées avec le périmètre PAB01 (envoi B2C + avoirs).
/// </summary>
public sealed class B2BrouterClientCapabilityTests
{
    [Fact]
    public void Declared_Capabilities_Match_The_Finalized_B2Brouter_State()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var caps = B2BrouterTestData.CreateClient(handler).Capabilities;

        caps.PaName.Should().Be("B2Brouter");
        caps.SupportsB2cReporting.Should().BeTrue("l'envoi B2C est livré par PAB01");
        caps.SupportsCreditNotes.Should().BeTrue("les avoirs sont livrés par PAB01");
        caps.SupportsTaxReportRetrieval.Should().BeTrue("List/Get tax reports + réglage idempotent livrés par PAB03");

        // Endpoint/flux non confirmés en staging → déclaration honnête false (CLAUDE.md n°2/3 ; vérif PAB04) :
        caps.SupportsDocumentRetrieval.Should().BeFalse("endpoint de téléchargement non confirmé en staging (PAB03 §4)");
        caps.SupportsReportRectification.Should().BeFalse("flux RE à vérifier en staging (PIP04 / PAB03 §5)");

        // Capacités réellement absentes de B2Brouter (état 2026-06) :
        caps.SupportsDomesticPaymentReporting.Should().BeFalse("flux 10.4 absent de B2Brouter (F09)");
        caps.SupportsInternationalPaymentReporting.Should().BeFalse("flux 10.2 absent de B2Brouter (F09)");
        caps.SupportsB2bInvoicing.Should().BeFalse("phase 2");
        caps.SupportsSelfBilling.Should().BeFalse("émission 389 non confirmée en staging — déclaration honnête (MND07 / F15 §1.8)");
        caps.SupportsMarginAmountReporting.Should().BeFalse("montant de marge (cas n°33) non confirmé côté B2Brouter — déclaration honnête (B2C09a)");
    }

    [Fact]
    public async Task SelfBilled389_Without_Capability_Degrades_To_Typed_Result_And_Does_Not_Call_The_Pa()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler); // V1 : SupportsSelfBilling = false

        var result = await client.SendDocumentAsync(
            B2BrouterTestData.Invoice20("F-389"),
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
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);
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
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.GetGeneratedDocumentAsync("INV-1001");

        result.Content.Should().BeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
    }

    [Fact]
    public async Task SendCreditNote_Without_Capability_Degrades_To_Typed_Result_And_Does_Not_Call_The_Pa()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var restricted = B2BrouterTestData.CreateClient(handler).Capabilities with { SupportsCreditNotes = false };
        var client = B2BrouterTestData.CreateClient(handler, restricted);

        var result = await client.SendDocumentAsync(B2BrouterTestData.CreditNote());

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.CreditNotes);
        handler.CallCount.Should().Be(0, "un avoir non supporté ne part jamais sur le réseau");
    }
}
