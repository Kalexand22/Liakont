namespace Liakont.Modules.Pipeline.Tests.Integration.Send;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// SEND (PIP01c) bout en bout sur une base tenant PostgreSQL réelle, avec archive WORM réelle, staging réel
/// et plug-in PA factice : émission → archive → purge subordonnée au WORM, conservation du staging sur rejet,
/// et anti-doublon (raccrochage d'un Sending sans renvoi / déduplication PA au renvoi). Tests sériés (une
/// base partagée) : chaque test utilise des identifiants/numéros uniques et son propre plug-in factice.
/// </summary>
public sealed class SendTenantJobIntegrationTests : IClassFixture<PipelineSendHarness>
{
    private readonly PipelineSendHarness _harness;

    public SendTenantJobIntegrationTests(PipelineSendHarness harness) => _harness = harness;

    [Fact]
    public async Task ReadyToSend_Document_Is_Issued_Archived_And_Staging_Purged()
    {
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("send-ok-" + documentId.ToString("N"), "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(pivot.Number);
        (await _harness.IsStagedAsync(documentId, hash))
            .Should().BeFalse("le staging est purgé une fois le paquet WORM effectivement présent (ADR-0014 §4).");

        var runs = await _harness.GetRunsAsync();
        runs.Should().Contain(r => r.RunType == Liakont.Modules.Pipeline.Contracts.PipelineRunType.Send);
    }

    [Fact]
    public async Task Rejected_Document_Keeps_Staging()
    {
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { SendScenario = FakePaScenario.Rejected });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("send-reject-" + documentId.ToString("N"), "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("RejectedByPa");
        (await _harness.IsStagedAsync(documentId, hash))
            .Should().BeTrue("un rejet PA conserve le contenu stagé (correction/resoumission — jamais archivé en WORM).");
    }

    [Fact]
    public async Task Issued_But_Worm_Absent_Keeps_Staging()
    {
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = true;
        try
        {
            var documentId = Guid.NewGuid();
            var pivot = CheckIntegrationFixtures.BuildPivot("send-worm-" + documentId.ToString("N"), "NORMAL");
            var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
            await _harness.MarkReadyToSendAsync(documentId);

            await _harness.RunSendAsync();

            (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");
            (await _harness.IsStagedAsync(documentId, hash))
                .Should().BeTrue("entre la transition Issued et l'écriture WORM, le staging est CONSERVÉ (purge subordonnée au WORM, jamais à l'étiquette Issued).");
        }
        finally
        {
            _harness.ForceWormAbsent = false;
        }
    }

    [Fact]
    public async Task Sending_Already_Known_By_Pa_Is_Finalized_Without_Resending()
    {
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("send-antidup-" + documentId.ToString("N"), "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        await _harness.BeginSendingAsync(documentId);
        await _harness.SetPaDocumentIdAsync(documentId, "FAKE-" + pivot.Number);

        // Cycle N : la PA a émis le document (réponse perdue → resté Sending côté plateforme).
        await _harness.PaClient.SendDocumentAsync(pivot);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");
        _harness.PaCallCount(nameof(IPaClient.GetDocumentStatusAsync), "FAKE-" + pivot.Number)
            .Should().BeGreaterThan(0, "la PA est interrogée avant tout renvoi.");
        _harness.PaCallCount(nameof(IPaClient.SendDocumentAsync), pivot.Number)
            .Should().Be(1, "un document déjà connu de la PA n'est PAS renvoyé (seul l'envoi du cycle N a eu lieu).");
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeFalse();
    }

    [Fact]
    public async Task Crashed_Sending_Without_Reference_Is_Recovered_Without_Duplicate_Emission()
    {
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("send-resend-" + documentId.ToString("N"), "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        await _harness.BeginSendingAsync(documentId);

        // Cycle N : la PA a émis le document, mais la plateforme a planté avant d'enregistrer la référence.
        await _harness.PaClient.SendDocumentAsync(pivot);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");

        // Sans référence connue, le document est RENVOYÉ (1 renvoi en plus de l'envoi du cycle N) ; la PA le
        // déduplique par numéro (F05, _issued est un ensemble) → aucune double émission, document Issued.
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(pivot.Number);
        _harness.PaCallCount(nameof(IPaClient.SendDocumentAsync), pivot.Number)
            .Should().Be(2, "le crash sans référence force un renvoi (dédoublonné par la PA) — exactement le cycle N + le raccrochage.");
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeFalse();
    }

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

    [Fact]
    public async Task TaxReportSetting_Not_Published_Sends_Nothing()
    {
        _harness.UseUnpublishedFake();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("send-unpublished-" + documentId.ToString("N"), "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "SIREN non publié = aucun envoi à l'aveugle (« Transport not available », F04 §3.1).");
        _harness.PaClient.IssuedDocumentNumbers.Should().BeEmpty("le diagnostic court-circuite avant tout envoi (plug-in factice neuf).");
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeTrue();
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
