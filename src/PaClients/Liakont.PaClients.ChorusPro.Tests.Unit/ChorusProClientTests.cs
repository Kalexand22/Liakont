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
    // Les comportements de SQUELETTE testés ici ne touchent pas le réseau (résultats typés / no-op / lectures
    // gardées) : le client est construit avec un transport authentifié inerte (handler non sollicité + jeton
    // stub + en-tête cpro-account fictif). La double auth elle-même est couverte par ChorusProSendWithAuthTests.
    private static ChorusProClient NewClient() => new(
        new HttpClient(new RecordingHttpMessageHandler()),
        new StubChorusProTokenProvider(),
        technicalAccountHeader: "bG9naW46bWRw",
        ChorusProCapabilities.Declared);

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
    public async Task SendDocument_Without_Artifact_Is_Blocked_Never_Regenerated()
    {
        // CP04 : Chorus Pro = transport pur d'un Factur-X scellé. Sans artefact (contexte nul), le dépôt
        // est BLOQUÉ (jamais régénéré, jamais émis « à vide » — CLAUDE.md n°3/6), pas un faux « émis ».
        var result = await NewClient().SendDocumentAsync(MinimalDocument());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.State.Should().NotBe(PaSendState.Issued);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("CPRO_ARTEFACT_REQUIS");
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
