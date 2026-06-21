namespace Liakont.Modules.Pipeline.Tests.Integration.Send;

using System;
using System.Threading.Tasks;
using FluentAssertions;
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
    public async Task Async_Pa_Reference_Recorded_While_Sending_Persists_And_Is_Never_Redeposited()
    {
        // PIPE01 round-trip RÉEL (contraste avec SetPaDocumentIdAsync qui contourne la machine à états) : une PA
        // asynchrone accepte le dépôt et renvoie un n° de flux → RecordPaSendingReferenceAsync persiste la
        // référence sur le document RESTÉ Sending + inscrit un fait d'audit DocumentPaReferenceRecorded. Au cycle
        // suivant, la PA ne CONFIRME pas encore l'émission (statut non terminal) : le document est MAINTENU
        // Sending et n'est JAMAIS re-déposé — une PA asynchrone (Chorus Pro) créerait un nouveau flux à chaque
        // dépôt = double déclaration fiscale (CLAUDE.md n°3).
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("send-async-" + documentId.ToString("N"), "NORMAL");
        var hash = await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        await _harness.BeginSendingAsync(documentId);

        var flux = "FLUX-" + pivot.Number;
        await _harness.RecordPaSendingReferenceAsync(documentId, flux, "{\"etatCourantFlux\":\"Reçu\"}");

        // 1) Round-trip de persistance : référence posée, document RESTÉ Sending, fait d'audit inscrit.
        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Sending", "l'enregistrement de la référence ne change pas l'état.");
        (await _harness.GetPaDocumentIdAsync(documentId)).Should().Be(flux, "la référence de flux est persistée pour le raccrochage.");
        (await _harness.EventCountAsync(documentId, "DocumentPaReferenceRecorded"))
            .Should().Be(1, "un fait d'audit append-only matérialise l'accusé de réception asynchrone.");

        // 2) Cycle de raccrochage : la PA ne connaît pas (encore) ce flux (statut non terminal) → MAINTENU
        //    Sending, JAMAIS re-déposé.
        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Sending", "statut PA non terminal : le document reste Sending.");
        _harness.PaCallCount(nameof(IPaClient.GetDocumentStatusAsync), flux)
            .Should().BeGreaterThan(0, "le raccrochage interroge la PA par la référence persistée.");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(pivot.Number, "aucune (ré)émission.");
        _harness.PaCallCount(nameof(IPaClient.SendDocumentAsync), pivot.Number)
            .Should().Be(0, "un flux déjà accepté n'est JAMAIS re-déposé (anti double-dépôt).");
        (await _harness.IsStagedAsync(documentId, hash))
            .Should().BeTrue("rien n'est purgé tant que la PA n'a pas confirmé l'émission.");
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

        // FIX05 : un run qui n'envoie RIEN est tout de même journalisé avec le MOTIF opérateur (français,
        // action corrective) — il devient ainsi visible dans /traitements au lieu de ne vivre que dans les logs
        // serveur. Même garantie pour le chemin « aucun compte PA actif » (WriteRunLogAsync sur chaque sortie).
        var runs = await _harness.GetRunsAsync();
        runs.Should().Contain(
            r => r.RunType == Liakont.Modules.Pipeline.Contracts.PipelineRunType.Send
                 && r.Detail != null
                 && r.Detail.Contains("aucun envoi", StringComparison.Ordinal)
                 && r.Detail.Contains("Action opérateur", StringComparison.Ordinal),
            "le run sans envoi porte le motif opérateur dans le journal (FIX05).");
    }
}
