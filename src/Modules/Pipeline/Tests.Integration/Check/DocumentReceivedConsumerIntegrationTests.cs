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
    public async Task Classified_Margin_With_Professional_Buyer_Is_Routed_Not_Blocked_As_Unclassified()
    {
        // BUG-17 (volet a buyer-indépendant + volet b honoraire en ligne, F03 §2.10) : un document dont le RÉGIME
        // est CLASSÉ marge (adjudication E + VATEX-EU-J) avec un acheteur PROFESSIONNEL (SIREN) n'est PLUS happé par
        // la garde « marge non classée » — le CONTENU fiscal vient du régime (classé), le CANAL de l'acheteur. Le
        // portage de l'honoraire EN LIGNE (volet b) le rend B2B-représentable : router devient sûr (l'honoraire
        // n'est plus perdu). Le document est donc ROUTÉ en aval (atteint ReadyToSend), jamais bloqué « marge non
        // classée ». ANCIEN attendu : Blocked + motif « honoraires » ; NOUVEAU : ReadyToSend (routé en aval).
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var buyer = new PivotPartyDto("Galerie Pro SARL", siren: "945678902");
        var pivot = CheckIntegrationFixtures.BuildB2cMarginDeclaration(sourceReference, "NORMAL", customer: buyer);

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        await ConsumeAsync(documentId, sourceReference, CheckIntegrationFixtures.PayloadHashOf(pivot));

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be(
            "ReadyToSend",
            "un régime classé marge avec acheteur professionnel route en aval (buyer-indépendant), jamais bloqué « marge non classée » (BUG-17 volet a/b).");

        var events = await _harness.GetEventsAsync(documentId);
        events.Should().NotContain(
            e => e.Detail != null && e.Detail.Contains("marge sous-déclarée", StringComparison.Ordinal),
            "la garde fail-closed « marge non classée » ne doit PLUS firer sur un régime CLASSÉ (BUG-17 volet a).");
    }

    [Fact]
    public async Task Buyer_Country_Legacy_Alias_Is_Normalized_Before_BT55_So_The_Document_Is_Not_Blocked_On_Country()
    {
        // ADR-0038 (câblage CHECK) : un acheteur B2B dont le pays source est un code LEGACY non-ISO (« ENG »)
        // serait BLOQUÉ par BT-55 (BUYER_COUNTRY_INVALID) s'il arrivait brut. Le référentiel normalise ENG→GB au
        // read-time AVANT validation → BT-55 voit un code ISO valide. On assère l'ABSENCE du motif « pays » (isolé
        // du reste de la validation) : si le câblage de normalisation au CHECK était retiré, le document serait
        // bloqué avec BUYER_COUNTRY_INVALID et ce test ROUGIRAIT — anti faux-vert sur la voie CHECK.
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var buyer = new PivotPartyDto(
            "Acheteur UK Ltd",
            siren: "945678902",
            address: new PivotAddressDto(city: "London", countryCode: "ENG"));
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL", customer: buyer);

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        await ConsumeAsync(documentId, sourceReference, CheckIntegrationFixtures.PayloadHashOf(pivot));

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be(
            "ReadyToSend",
            "l'alias legacy ENG→GB (ADR-0038) est normalisé avant BT-55 (code ISO valide) → aucun blocage.");

        var events = await _harness.GetEventsAsync(documentId);
        events.Should().NotContain(
            e => e.Detail != null && e.Detail.Contains("pays", StringComparison.OrdinalIgnoreCase),
            "le pays acheteur normalisé (GB) est un code ISO valide : BT-55 ne doit poser AUCUN motif « pays ».");
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
