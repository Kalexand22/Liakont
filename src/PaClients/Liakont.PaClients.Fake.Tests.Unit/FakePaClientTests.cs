namespace Liakont.PaClients.Fake.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Comportement du plug-in factice (acceptance PAA02) : il couvre les cinq familles d'issue d'une PA
/// (succès / rejet / erreur silencieuse / timeout / capacité absente), journalise ses appels et reste
/// idempotent par numéro de document. Conformément à l'abstraction (PAA01), une capacité absente
/// retourne TOUJOURS un résultat typé, jamais une exception — c'est ce qui prouve que le produit n'est
/// jamais bloqué par une PA limitée.
/// </summary>
public sealed class FakePaClientTests
{
    [Fact]
    public void Default_Options_Declare_The_Generous_V1_Capabilities()
    {
        var client = new FakePaClient();

        client.Capabilities.PaName.Should().Be("Fake");
        client.Capabilities.SupportsB2cReporting.Should().BeTrue();
        client.Capabilities.SupportsDomesticPaymentReporting.Should().BeTrue();
        client.Capabilities.SupportsInternationalPaymentReporting.Should().BeFalse("V1 n'alimente que le flux domestique (D2)");
        client.Capabilities.SupportsCreditNotes.Should().BeTrue();
        client.Capabilities.SupportsDocumentRetrieval.Should().BeTrue();
    }

    [Fact]
    public async Task SendDocument_Success_Returns_Issued_And_Journals_The_Call()
    {
        var client = new FakePaClient();

        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-1"));

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("FAKE-F-1");
        client.Calls.Should().ContainSingle(c => c.Method == "SendDocumentAsync" && c.Detail == "F-1");
    }

    [Fact]
    public async Task SendDocument_WithoutSendAfterImport_Returns_New_And_Is_Not_Issued()
    {
        var client = new FakePaClient();

        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-10"), sendAfterImport: false);

        result.State.Should().Be(PaSendState.New);
        result.PaDocumentId.Should().Be("FAKE-F-10");
        client.IssuedDocumentNumbers.Should().BeEmpty("un document non envoyé n'est pas émis");
    }

    [Fact]
    public async Task SendDocument_CreditNote_When_Supported_Returns_Issued()
    {
        var client = new FakePaClient();

        var result = await client.SendDocumentAsync(TestDocuments.CreditNote("A-2"));

        result.State.Should().Be(PaSendState.Issued);
    }

    [Fact]
    public async Task SendDocument_CreditNote_When_Not_Supported_Returns_Typed_Gap_Never_Throws()
    {
        var caps = new PaCapabilities { PaName = "FakeLimité", SupportsCreditNotes = false };
        var client = new FakePaClient(new FakePaClientOptions { Capabilities = caps });

        var result = await client.SendDocumentAsync(TestDocuments.CreditNote("A-3"));

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported.Should().NotBeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.CreditNotes);
        result.CapabilityNotSupported.PaName.Should().Be("FakeLimité");
        result.CapabilityNotSupported.OperatorMessage.Should().Contain("FakeLimité");
    }

    [Fact]
    public async Task SendPaymentReport_Domestic_Is_Supported_By_Default()
    {
        var client = new FakePaClient();
        var period = new PaymentReportPeriod
        {
            Flux = PaymentReportFlux.Domestic,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await client.SendPaymentReportAsync(period);

        result.State.Should().Be(PaSendState.Issued);
        client.Calls.Should().ContainSingle(c => c.Method == "SendPaymentReportAsync" && c.Detail == "Domestic");
    }

    [Fact]
    public async Task SendPaymentReport_International_Not_Supported_By_Default_Returns_Typed_Gap()
    {
        var client = new FakePaClient();
        var period = new PaymentReportPeriod
        {
            Flux = PaymentReportFlux.International,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await client.SendPaymentReportAsync(period);

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.InternationalPaymentReporting);
    }

    [Fact]
    public async Task SendDocument_Rejected_Scenario_Surfaces_Errors_Intact()
    {
        var errors = new[] { new PaError("E_VAT", "Numéro de TVA invalide.") };
        var client = new FakePaClient(new FakePaClientOptions
        {
            SendScenario = FakePaScenario.Rejected,
            RejectionErrors = errors,
        });

        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-2"));

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("E_VAT");
        client.IssuedDocumentNumbers.Should().BeEmpty("un rejet n'est pas une émission");
    }

    [Fact]
    public async Task SendDocument_SilentError_Scenario_Is_Detected_As_Rejected_Despite_Http_200()
    {
        var client = new FakePaClient(new FakePaClientOptions { SendScenario = FakePaScenario.SilentError });

        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-3"));

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().NotBeEmpty();
        result.RawResponse.Should().Contain("200", "l'erreur silencieuse est un HTTP 200 + errors[]");
    }

    [Fact]
    public async Task SendDocument_TechnicalError_Scenario_Is_Retryable()
    {
        var client = new FakePaClient(new FakePaClientOptions { SendScenario = FakePaScenario.TechnicalError });

        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-4"));

        result.State.Should().Be(PaSendState.TechnicalError);
    }

    [Fact]
    public async Task SendDocument_Timeout_Scenario_Is_Retryable_TechnicalError()
    {
        var client = new FakePaClient(new FakePaClientOptions { SendScenario = FakePaScenario.Timeout });

        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-5"));

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().Contain(e => e.Code == "FAKE_TIMEOUT");
    }

    [Fact]
    public async Task SendDocument_Same_Number_Twice_Is_Idempotent_Never_Sent_Twice()
    {
        var client = new FakePaClient();

        var first = await client.SendDocumentAsync(TestDocuments.Invoice("F-DUP"));
        var second = await client.SendDocumentAsync(TestDocuments.Invoice("F-DUP"));

        first.State.Should().Be(PaSendState.Issued);
        second.PaDocumentId.Should().Be(first.PaDocumentId, "le même numéro retourne le résultat d'origine");
        client.IssuedDocumentNumbers.Should().ContainSingle().Which.Should().Be("F-DUP");
        client.Calls.Where(c => c.Method == "SendDocumentAsync").Should().HaveCount(2, "les deux appels sont journalisés");
    }

    [Fact]
    public async Task GetGeneratedDocument_When_Supported_Returns_Content()
    {
        var client = new FakePaClient();

        var generated = await client.GetGeneratedDocumentAsync("FAKE-F-1");

        generated.Content.Should().NotBeNullOrEmpty();
        generated.Format.Should().Be("Factur-X");
        generated.CapabilityNotSupported.Should().BeNull();
    }

    [Fact]
    public async Task GetGeneratedDocument_When_Not_Supported_Returns_Typed_Gap_Never_Throws()
    {
        var caps = new PaCapabilities { PaName = "FakeLimité", SupportsDocumentRetrieval = false };
        var client = new FakePaClient(new FakePaClientOptions { Capabilities = caps });

        var generated = await client.GetGeneratedDocumentAsync("FAKE-X");

        generated.Content.Should().BeNull();
        generated.CapabilityNotSupported.Should().NotBeNull();
        generated.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
    }

    [Fact]
    public async Task EnsureTaxReportSetting_Then_Get_Reflects_The_Request_Idempotently()
    {
        var client = new FakePaClient();
        var request = new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "VENTE",
            EnterpriseSize = "PME",
            NafCode = "6820A",
            CinScheme = "0002",
        };

        await client.EnsureTaxReportSettingAsync(request);
        await client.EnsureTaxReportSettingAsync(request);
        var setting = await client.GetTaxReportSettingAsync();

        setting.StartDate.Should().Be(new DateOnly(2026, 1, 1));
        setting.TypeOperation.Should().Be("VENTE");
        setting.NafCode.Should().Be("6820A");
        setting.CinScheme.Should().Be("0002");
    }

    [Fact]
    public async Task A_Cancelled_Token_Throws_OperationCanceled()
    {
        var client = new FakePaClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await client.SendDocumentAsync(TestDocuments.Invoice("F-9"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
