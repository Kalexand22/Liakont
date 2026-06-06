namespace Liakont.Modules.Pipeline.Tests.Integration.CreditNotes;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// PIP02 — un avoir vers une PA SANS capacité avoirs reste <c>ReadyToSend</c> (jamais bloqué ni envoyé), traité
/// dès que la capacité sera déclarée (INV-PIPELINE-021/027). Base ISOLÉE par méthode (le test laisse un avoir en
/// <c>ReadyToSend</c> — un conteneur dédié évite toute pollution de la fixture partagée des autres tests SEND).
/// </summary>
public sealed class CreditNoteCapabilityTests : IAsyncLifetime
{
    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task CreditNote_To_Pa_Without_Capability_Stays_ReadyToSend_And_Is_Never_Sent()
    {
        // PA publiée mais ne déclarant PAS la capacité avoirs : un avoir reste ReadyToSend (jamais bloqué ni
        // envoyé à l'aveugle), traité dès que la capacité sera déclarée (INV-PIPELINE-021, F07).
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutCreditNotes() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var avoir = CheckIntegrationFixtures.BuildCreditNote(
            "send-cn-nocap-" + documentId.ToString("N"),
            "NORMAL",
            new PivotDocumentRefDto("F-ORIG-NOCAP", new DateTime(2026, 1, 10)));
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, avoir);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "un avoir vers une PA sans capacité avoirs reste ReadyToSend — jamais bloqué ni envoyé à l'aveugle (INV-PIPELINE-021).");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(avoir.Number, "la capacité avoirs n'est pas déclarée : aucun envoi.");
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeTrue("le contenu de l'avoir est conservé tant qu'il n'est pas émis.");
    }

    /// <summary>Capacités d'une PA publiée générale MAIS sans la capacité avoirs (le reste = défaut V1).</summary>
    private static PaCapabilities WithoutCreditNotes() => new()
    {
        PaName = "Fake",
        SupportsB2cReporting = true,
        SupportsDomesticPaymentReporting = true,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = false,
        SupportsTaxReportRetrieval = true,
        SupportsDocumentRetrieval = true,
        SupportsReportRectification = true,
        MaxDocumentsPerRequest = null,
    };
}
