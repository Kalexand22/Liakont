namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Staging.Contracts;
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

    [Fact]
    public async Task Absent_Staging_Is_Transient_Then_Reaches_ReadyToSend_After_Re_Stage()
    {
        // FIX07b : un document Detected dont le contenu stagé a disparu (perte de staging) ne doit ni se bloquer
        // ni se perdre. CHECK est TRANSITOIRE (ADR-0014) : il propage StagedPayloadNotFoundException pour que
        // l'outbox re-livre, le document RESTE Detected. Une fois le contenu RE-STAGÉ (réhydratation au re-push
        // de l'agent), le CHECK est déroulable et le document avance — plus de zombie.
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);

        // Document Detected mais contenu PAS (ou plus) stagé.
        await _harness.SeedDetectedDocumentAsync(documentId, sourceReference, hash, pivot);

        Func<Task> checkWithoutStaging = () => ConsumeAsync(documentId, sourceReference, hash);
        await checkWithoutStaging.Should().ThrowAsync<StagedPayloadNotFoundException>();
        (await _harness.GetDocumentStateAsync(documentId)).Should().Be(
            "Detected", "contenu stagé absent = transitoire (ADR-0014), jamais un blocage terminal inventé");

        // Réhydratation du contenu (re-push agent) puis CHECK : le document est de nouveau traitable.
        await _harness.StagePayloadAsync(documentId, hash, json);
        await ConsumeAsync(documentId, sourceReference, hash);

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be(
            "ReadyToSend", "le contenu re-stagé rend le CHECK déroulable — plus de zombie définitif (FIX07b)");
    }

    [Fact]
    public async Task Margin_Shaped_Document_Not_Classified_Margin_Is_Blocked_Fail_Closed()
    {
        // Garde fail-closed (review P2 / CLAUDE.md n°3) : un document à la FORME d'une marge (honoraires + aucune
        // TVA distincte) que le mapping NE classe PAS marge — ici adjudication exonérée (E + VATEX-EU-J) MAIS
        // acheteur PROFESSIONNEL (SIREN) → ni B2C marge, ni B2B représentable (honoraires hors lignes) — est
        // BLOQUÉ au CHECK avec un message opérateur, jamais routé en silence (honoraires perdus = marge sous-déclarée).
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var buyer = new PivotPartyDto("Galerie Pro SARL", siren: "945678902");
        var pivot = CheckIntegrationFixtures.BuildB2cMarginDeclaration(sourceReference, "NORMAL", customer: buyer);

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        await ConsumeAsync(documentId, sourceReference, CheckIntegrationFixtures.PayloadHashOf(pivot));

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Blocked");

        var events = await _harness.GetEventsAsync(documentId);
        events.Should().Contain(
            e => e.Detail != null && e.Detail.Contains("honoraires", StringComparison.Ordinal),
            "le motif fail-closed « marge non classée » doit être consigné dans la piste d'audit (CLAUDE.md n°12).");
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
