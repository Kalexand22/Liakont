namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre le comportement du SQUELETTE Chorus Pro (CP02) : capacité absente → résultat TYPÉ (jamais
/// d'exception, invariant PAA01) ; les méthodes de réglage appelées HORS garde de capacité dégradent
/// fail-closed (vide / no-op) ; les lectures de tax reports gardées par capacité lèvent une exception
/// traçable plutôt que de mentir (CLAUDE.md n°2/3).
/// </summary>
public sealed class ChorusProClientTests
{
    private static ChorusProClient NewClient() => new(ChorusProCapabilities.Declared);

    private static PivotDocumentDto MinimalDocument() => new(
        sourceDocumentKind: "FA",
        number: "CPRO-TEST-1",
        issueDate: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        sourceReference: "REF-1",
        supplier: null,
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: null);

    [Fact]
    public void Capabilities_Are_All_False_In_The_Skeleton()
    {
        var caps = NewClient().Capabilities;

        caps.PaName.Should().Be("Chorus Pro");
        caps.SupportsFacturXTransmission.Should().BeFalse();
        caps.SupportsB2bInvoicing.Should().BeFalse();
        caps.SupportsB2cReporting.Should().BeFalse();
        caps.SupportsTaxReportRetrieval.Should().BeFalse();
        caps.SupportsDocumentRetrieval.Should().BeFalse();
    }

    [Fact]
    public async Task SendDocument_Returns_Typed_NotSupported_For_FacturX_Transmission()
    {
        var result = await NewClient().SendDocumentAsync(MinimalDocument());

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.FacturXTransmission);
        result.CapabilityNotSupported.PaName.Should().Be("Chorus Pro");
    }

    [Fact]
    public async Task SendDocument_With_Null_Document_Throws()
    {
        var act = async () => await NewClient().SendDocumentAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(PaymentReportFlux.Domestic, PaCapability.DomesticPaymentReporting)]
    [InlineData(PaymentReportFlux.International, PaCapability.InternationalPaymentReporting)]
    public async Task SendPaymentReport_Returns_Typed_NotSupported(PaymentReportFlux flux, PaCapability expected)
    {
        var period = new PaymentReportPeriod
        {
            Flux = flux,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await NewClient().SendPaymentReportAsync(period);

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(expected);
    }

    [Fact]
    public async Task GetGeneratedDocument_Returns_Typed_NotSupported_For_DocumentRetrieval()
    {
        var result = await NewClient().GetGeneratedDocumentAsync("CPRO-1");

        result.Content.Should().BeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
    }

    [Fact]
    public async Task GetDocumentStatus_Is_Fail_Closed_TechnicalError_Never_A_Fake_Fiscal_State()
    {
        var status = await NewClient().GetDocumentStatusAsync("CPRO-1");

        status.PaDocumentId.Should().Be("CPRO-1");
        status.State.Should().Be(PaSendState.TechnicalError);
        status.State.Should().NotBe(PaSendState.Issued, "le squelette n'invente jamais un état fiscal (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task GetTaxReportSetting_Returns_An_Empty_Inactive_Setting_Fail_Closed()
    {
        var setting = await NewClient().GetTaxReportSettingAsync();

        // Réglage vide → SIREN non publié : l'envoi reste bloqué proprement (jamais un faux « actif »).
        setting.StartDate.Should().BeNull();
        setting.IsActiveOn(new DateOnly(2026, 1, 15)).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureTaxReportSetting_Is_A_No_Op_And_Never_Throws()
    {
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "BIENS",
            EnterpriseSize = "PME",
        };

        var act = async () => await NewClient().EnsureTaxReportSettingAsync(request);

        await act.Should().NotThrowAsync("appelée hors garde de capacité, ne doit jamais lever (PAA01)");
    }

    [Fact]
    public async Task Tax_Report_Reads_Throw_A_Traceable_NotImplemented_Rather_Than_Lying()
    {
        var client = NewClient();

        await ((Func<Task>)(() => client.ListTaxReportsAsync())).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetTaxReportAsync("TR-1"))).Should().ThrowAsync<NotImplementedException>();
        await ((Func<Task>)(() => client.GetAccountInfoAsync())).Should().ThrowAsync<NotImplementedException>();
    }
}
