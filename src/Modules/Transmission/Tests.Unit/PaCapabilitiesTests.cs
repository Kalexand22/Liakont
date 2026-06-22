namespace Liakont.Modules.Transmission.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Tests.Unit.TestDoubles;
using Xunit;

/// <summary>
/// Couvre l'invariant produit de PAA01 : le comportement est piloté par les capacités déclarées
/// (flux 10.2/10.4 distincts) et une capacité absente retourne un résultat TYPÉ journalisable,
/// jamais une exception ni un blocage (CLAUDE.md n°6/8/16 ; INV-TRANSMISSION-001/002).
/// </summary>
public sealed class PaCapabilitiesTests
{
    [Fact]
    public void SupportsPaymentReport_DistinguishesDomesticAndInternationalFlux()
    {
        // Une PA qui ne supporte QUE le domestique (flux 10.4) — les deux flux sont distincts (F01-F02 §1).
        var capabilities = new PaCapabilities
        {
            PaName = "FakePa",
            SupportsDomesticPaymentReporting = true,
            SupportsInternationalPaymentReporting = false,
        };

        capabilities.SupportsPaymentReport(PaymentReportFlux.Domestic).Should().BeTrue();
        capabilities.SupportsPaymentReport(PaymentReportFlux.International).Should().BeFalse();
    }

    [Fact]
    public async Task SendPaymentReportAsync_UnsupportedFlux_ReturnsTypedCapabilityGap_NoException()
    {
        var client = new StubPaClient(new PaCapabilities
        {
            PaName = "FakePa",
            SupportsDomesticPaymentReporting = true,
            SupportsInternationalPaymentReporting = false,
        });

        var period = new PaymentReportPeriod
        {
            Flux = PaymentReportFlux.International,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await client.SendPaymentReportAsync(period);

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported.Should().NotBeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.InternationalPaymentReporting);
        result.PaDocumentId.Should().BeNull("la PA ne peut rien émettre quand la capacité manque");
    }

    [Fact]
    public async Task SendPaymentReportAsync_SupportedFlux_IsIssued()
    {
        var client = new StubPaClient(new PaCapabilities
        {
            PaName = "FakePa",
            SupportsDomesticPaymentReporting = true,
        });

        var period = new PaymentReportPeriod
        {
            Flux = PaymentReportFlux.Domestic,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await client.SendPaymentReportAsync(period);

        result.State.Should().Be(PaSendState.Issued);
        result.CapabilityNotSupported.Should().BeNull();
    }

    [Fact]
    public async Task GetGeneratedDocumentAsync_WhenRetrievalUnsupported_ReturnsTypedGap_NoContent_NoException()
    {
        var client = new StubPaClient(new PaCapabilities
        {
            PaName = "FakePa",
            SupportsDocumentRetrieval = false,
        });

        var result = await client.GetGeneratedDocumentAsync("PA-DOC-1");

        result.CapabilityNotSupported.Should().NotBeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
        result.Content.Should().BeNull();
    }

    [Fact]
    public void RequireMarginAmountReporting_GateOpen_WhenCapabilityDeclared()
    {
        // Gate OUVERT (B2C09a) : une PA déclarant la capacité ne produit aucun écart (null) — l'aval marge
        // (B2C09b) peut transmettre. Piloté par la capacité déclarée, jamais par un if (pa is …).
        var capabilities = new PaCapabilities { PaName = "FakePa", SupportsMarginAmountReporting = true };

        capabilities.RequireMarginAmountReporting().Should().BeNull("la capacité est déclarée : le gate est ouvert");
    }

    [Fact]
    public void RequireMarginAmountReporting_GateClosed_ReturnsTypedResult_NoException()
    {
        // Gate FERMÉ (B2C09a) : capacité absente → résultat TYPÉ journalisable, jamais une exception
        // ni un blocage du produit (PAA01 ; CLAUDE.md n°3/8). La FORME du payload reste gelée (B2C09b).
        var capabilities = new PaCapabilities { PaName = "B2Brouter", SupportsMarginAmountReporting = false };

        var gap = capabilities.RequireMarginAmountReporting();

        gap.Should().NotBeNull("la capacité n'est pas déclarée : le gate est fermé");
        gap!.Capability.Should().Be(PaCapability.MarginAmountReporting);
        gap.PaName.Should().Be("B2Brouter");
        gap.OperatorMessage.Should().Contain("B2Brouter");
        gap.OperatorMessage.Should().Contain("montant de la marge");
    }

    [Fact]
    public void CapabilityNotSupportedResult_OperatorMessage_IsFrench_AndJournalisable()
    {
        var gap = PaCapabilityNotSupportedResult.Create("B2Brouter", PaCapability.DocumentRetrieval);

        gap.PaName.Should().Be("B2Brouter");
        gap.Capability.Should().Be(PaCapability.DocumentRetrieval);

        // Message opérateur en français (CLAUDE.md n°12), portant le nom de PA et le libellé de capacité.
        gap.OperatorMessage.Should().Contain("B2Brouter");
        gap.OperatorMessage.Should().Contain("ne prend pas encore en charge");
        gap.OperatorMessage.Should().Contain("facture générée");
    }

    [Fact]
    public async Task SendB2cTransaction_WithoutB2cReporting_ReturnsTypedGap_NoException()
    {
        // Implémentation PAR DÉFAUT de IPaClient : une PA sans la capacité B2C dégrade en résultat typé.
        IPaClient client = new StubPaClient(new PaCapabilities { PaName = "FakePa", SupportsB2cReporting = false });

        var result = await client.SendB2cTransactionAsync(MarginTransaction());

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.B2cReporting);
    }

    [Fact]
    public async Task SendB2cTransaction_Margin_GatedOnDedicatedMarginCapability_NotOnB2cReporting()
    {
        // Régression review (P1) : le montant de marge (TMA1) se garde sur SupportsMarginAmountReporting,
        // JAMAIS sur le seul SupportsB2cReporting — sinon on transmettrait une forme de marge non confirmée.
        IPaClient client = new StubPaClient(new PaCapabilities
        {
            PaName = "SuperPdp",
            SupportsB2cReporting = true,
            SupportsMarginAmountReporting = false,
        });

        var result = await client.SendB2cTransactionAsync(MarginTransaction());

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(
            PaCapability.MarginAmountReporting,
            "la marge se garde sur la capacité DÉDIÉE, pas sur la capacité B2C générique");
    }

    private static B2cReportingTransaction MarginTransaction() => new()
    {
        Category = EReportingTransactionCategory.Tma1,
        Role = EReportingDeclarantRole.Seller,
        CurrencyCode = "EUR",
        Date = new DateOnly(2026, 6, 22),
        TaxExclusiveAmount = 100.00m,
        TaxTotal = 20.00m,
        Subtotals = [new B2cReportingTransactionSubtotal { TaxPercent = 20.0m, TaxableAmount = 100.00m, TaxTotal = 20.00m }],
    };
}
