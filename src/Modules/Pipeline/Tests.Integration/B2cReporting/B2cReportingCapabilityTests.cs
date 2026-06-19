namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// B2C01 — routage de la DÉCLARATION e-reporting B2C (flux 10.3) par la voie document unique
/// (<see cref="IPaClient.SendDocumentAsync"/>) avec une garde de capacité CIBLÉE : une déclaration 10.3 vers
/// une PA sans <c>SupportsB2cReporting</c> reste <c>ReadyToSend</c> (maintenue, jamais transmise), tandis
/// qu'une facture ORDINAIRE (sans marqueur 10.3) vers la MÊME PA est TOUJOURS transmise (pas de régression de
/// la voie unique). Base ISOLÉE par méthode (un document maintenu en <c>ReadyToSend</c> ne doit pas polluer
/// la fixture partagée des autres tests SEND).
/// </summary>
public sealed class B2cReportingCapabilityTests : IAsyncLifetime
{
    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task B2cReportingDeclaration_To_Pa_Without_Capability_Stays_ReadyToSend_And_Is_Never_Sent()
    {
        // PA publiée mais ne déclarant PAS l'e-reporting B2C : une déclaration 10.3 reste ReadyToSend (jamais
        // transmise sans la capacité — résultat typé journalisé, CLAUDE.md n°3). Garde CIBLÉE sur le marqueur 10.3.
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2cReporting() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(
            "send-b2c-nocap-" + documentId.ToString("N"),
            "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "une déclaration 10.3 vers une PA sans capacité B2C reste ReadyToSend — jamais transmise sans la capacité.");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(declaration.Number, "la capacité B2C n'est pas déclarée : aucun envoi.");
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeTrue("le contenu de la déclaration est conservé tant qu'elle n'est pas émise.");
    }

    [Fact]
    public async Task Ordinary_Invoice_To_Pa_Without_B2cReporting_Is_Transmitted()
    {
        // NON-RÉGRESSION : la garde 10.3 est CIBLÉE. Une facture ORDINAIRE (sans marqueur 10.3) vers la MÊME PA
        // sans capacité B2C est TOUJOURS transmise — la voie unique SendDocumentAsync des autres flux est intacte.
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2cReporting() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var invoice = CheckIntegrationFixtures.BuildPivot("send-ordinary-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, invoice);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("Issued", "une facture ordinaire (sans marqueur 10.3) n'est jamais touchée par la garde B2C.");
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(invoice.Number);
    }

    [Fact]
    public async Task B2cReportingDeclaration_To_Pa_With_Capability_Is_Routed_And_Issued()
    {
        // ROUTAGE NOMINAL : une déclaration 10.3 vers une PA qui DÉCLARE l'e-reporting B2C (défaut V1) est routée
        // par la voie document unique et émise — la garde ciblée ne bloque pas quand la capacité est présente.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(
            "send-b2c-ok-" + documentId.ToString("N"),
            "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("Issued", "une déclaration 10.3 vers une PA capable est routée et émise.");
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(declaration.Number);
    }

    /// <summary>Capacités d'une PA publiée générale MAIS sans la capacité e-reporting B2C (le reste = défaut V1).</summary>
    private static PaCapabilities WithoutB2cReporting() => new()
    {
        PaName = "Fake",
        SupportsB2cReporting = false,
        SupportsDomesticPaymentReporting = true,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = true,
        SupportsTaxReportRetrieval = true,
        SupportsDocumentRetrieval = true,
        SupportsReportRectification = true,
        SupportsSelfBilling = true,
        MaxDocumentsPerRequest = null,
    };
}
