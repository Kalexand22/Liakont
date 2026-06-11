namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// CHECK bout en bout sur une base tenant PostgreSQL réelle (INV-PIPELINE-008/011/012) : staging relu →
/// mapping contre une table validée persistée → cycle de vie réel (transition + audit append-only) →
/// journal d'exécutions. Les deux tests partagent l'état seedé (profil tenant + table validée pour le
/// régime « NORMAL ») et n'agissent que sur leur propre document.
/// </summary>
public sealed class DocumentReceivedConsumerIntegrationTests : IClassFixture<PipelineCheckHarness>
{
    private readonly PipelineCheckHarness _harness;

    public DocumentReceivedConsumerIntegrationTests(PipelineCheckHarness harness) => _harness = harness;

    [Fact]
    public async Task Mapped_And_Valid_Document_Reaches_ReadyToSend_And_Writes_RunLog()
    {
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        await ConsumeAsync(documentId, sourceReference, CheckIntegrationFixtures.PayloadHashOf(pivot));

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend");

        var runs = await _harness.GetRunsAsync();
        var run = runs.Single(r => r.Detail != null && r.Detail.Contains(documentId.ToString(), StringComparison.Ordinal));
        run.RunType.Should().Be(PipelineRunType.Check);
        run.Trigger.Should().Be(PipelineRunTrigger.Event);
        run.DocumentsSucceeded.Should().Be(1);
        run.DocumentsFailed.Should().Be(0);
    }

    [Fact]
    public async Task Unmapped_Regime_Blocks_Document_And_Persists_Motif_In_Audit_Trail()
    {
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "INCONNU");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        await ConsumeAsync(documentId, sourceReference, CheckIntegrationFixtures.PayloadHashOf(pivot));

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Blocked");

        var events = await _harness.GetEventsAsync(documentId);
        events.Should().Contain(
            e => e.Detail != null && e.Detail.Contains("table de mapping", StringComparison.Ordinal),
            "le motif de blocage doit être consigné dans la piste d'audit append-only (INV-PIPELINE-011)");

        var runs = await _harness.GetRunsAsync();
        var run = runs.Single(r => r.Detail != null && r.Detail.Contains(documentId.ToString(), StringComparison.Ordinal));
        run.DocumentsFailed.Should().Be(1);
        run.DocumentsSucceeded.Should().Be(0);
    }

    [Fact]
    public async Task Unmapped_Regime_And_Professional_Buyer_Surfaces_Both_Motifs_In_Single_Block()
    {
        // FIX06 (D5) : un document cumulant deux causes INDÉPENDANTES — régime non couvert (mapping) ET acheteur
        // « pro » (garde-fou B2B/B2C, indépendant du mapping) — montre les DEUX motifs dès le PREMIER CHECK, au
        // lieu de les découvrir l'un après l'autre. Vérifie l'agrégation contre les VRAIES règles de validation.
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(
            sourceReference, regimeCode: "INCONNU", customer: CheckIntegrationFixtures.ProfessionalBuyer());

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        await ConsumeAsync(documentId, sourceReference, CheckIntegrationFixtures.PayloadHashOf(pivot));

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Blocked");

        var events = await _harness.GetEventsAsync(documentId);
        var motif = events
            .Where(e => e.Detail != null)
            .Select(e => e.Detail!)
            .FirstOrDefault(detail => detail.Contains("table de mapping", StringComparison.Ordinal));
        motif.Should().NotBeNull("le motif de blocage de mapping doit être consigné dans la piste d'audit (INV-PIPELINE-011)");
        motif.Should().Contain(
            "professionnel",
            "le motif INDÉPENDANT du mapping (garde-fou B2B/B2C) est agrégé au même blocage dès le premier CHECK (FIX06)");
    }

    private async Task SeedAndStageAsync(Guid documentId, string sourceReference, PivotDocumentDto pivot)
    {
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);
        await _harness.SeedDetectedDocumentAsync(documentId, sourceReference, hash, pivot);
        await _harness.StagePayloadAsync(documentId, hash, json);
    }

    private async Task ConsumeAsync(Guid documentId, string sourceReference, string payloadHash)
    {
        var consumer = new DocumentReceivedConsumer(_harness.ScopeFactory, NullLogger<DocumentReceivedConsumer>.Instance);
        await consumer.HandleAsync(CheckIntegrationFixtures.Event(documentId, sourceReference, payloadHash));
    }
}
