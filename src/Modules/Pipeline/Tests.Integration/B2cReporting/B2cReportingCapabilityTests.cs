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
/// B2C01 — aiguillage d'une DÉCLARATION e-reporting B2C (flux 10.3) : elle est DIFFÉRÉE de la voie document
/// (garde D1, jamais e-invoicée) et e-reportée par son JOB, où vit la garde de capacité — une déclaration vers
/// une PA sans <c>SupportsB2cReporting</c> n'est pas transmise (reste <c>ReadyToSend</c>), une déclaration vers
/// une PA capable est e-reportée. À l'inverse, une facture B2B (acheteur identifié) est transmise par la voie
/// document (e-invoicing). Base ISOLÉE par méthode. Voir [[b2c-egale-ereporting-partout]].
/// </summary>
public sealed class B2cReportingCapabilityTests : IAsyncLifetime
{
    private const string SendB2cTransaction = "SendB2cTransactionAsync";

    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task B2cReportingDeclaration_To_Pa_Without_Capability_Is_Not_Reported_And_Stays_ReadyToSend()
    {
        // PA publiée mais ne déclarant PAS l'e-reporting B2C : le job d'e-reporting ne transmet RIEN (garde de
        // capacité DANS le job) et le document reste ReadyToSend (repris quand la capacité sera là, CLAUDE.md n°3).
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2cReporting() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(
            "send-b2c-nocap-" + documentId.ToString("N"),
            "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, "Tlb1/Seller/20260120").Should()
            .Be(0, "sans la capacité e-reporting B2C, la déclaration n'est jamais transmise.");
        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "une déclaration B2C non transmise (capacité absente) reste ReadyToSend.");
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeTrue("le contenu de la déclaration est conservé tant qu'elle n'est pas émise.");
    }

    [Fact]
    public async Task B2b_Invoice_Is_Transmitted_By_The_Document_Path()
    {
        // Une facture B2B (acheteur à SIREN) n'est PAS une déclaration 10.3 : elle est transmise par la voie
        // document (e-invoicing), indépendamment de la capacité e-reporting B2C de la PA.
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2cReporting() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var invoice = CheckIntegrationFixtures.BuildPivot("send-b2b-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, invoice);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("Issued", "une facture B2B (acheteur identifié) part par la voie document (e-invoicing).");
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(invoice.Number);
    }

    [Fact]
    public async Task B2cReportingDeclaration_To_Pa_With_Capability_Is_Deferred_And_Reported_By_Its_Job()
    {
        // ROUTAGE NOMINAL : une déclaration 10.3 est DIFFÉRÉE de la voie document (garde D1) et e-reportée par son
        // job vers une PA qui déclare l'e-reporting B2C (défaut V1) — jamais e-invoicée par-document.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(
            "send-b2c-ok-" + documentId.ToString("N"),
            "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        // La voie document la DIFFÈRE (jamais e-invoicée).
        await _harness.RunSendAsync();
        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "une déclaration 10.3 est différée de la voie document (garde D1).");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(declaration.Number, "une déclaration 10.3 n'est jamais e-invoicée par-document.");

        // Son job l'e-reporte.
        await _harness.RunB2cPlainAsync();
        _harness.PaCallCount(SendB2cTransaction, "Tlb1/Seller/20260120").Should()
            .Be(1, "la déclaration B2C est e-reportée par son job vers une PA capable.");
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
