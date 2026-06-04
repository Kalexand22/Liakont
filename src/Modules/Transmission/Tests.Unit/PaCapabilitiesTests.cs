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
}
