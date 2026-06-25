namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

/// <summary>
/// Rejeu read-time du contenu d'un document (BUG-5) sur une base tenant PostgreSQL réelle : relit le pivot SOURCE
/// stagé et REJOUE le mapping via la SOURCE UNIQUE (CheckTvaMapping + table validée persistée) — exactement le
/// chemin qu'emprunte la console pour afficher les lignes (libellé, montants, régime source → catégorie/VATEX/taux)
/// AVANT transmission (états Bloqué / Prêt-à-envoyer). Partage l'état seedé du harnais (profil tenant + table
/// validée : régime « NORMAL » → S 20 %) et n'agit que sur son propre document.
/// </summary>
public sealed class DocumentContentReplayIntegrationTests : IClassFixture<PipelineCheckHarness>
{
    private readonly PipelineCheckHarness _harness;

    public DocumentContentReplayIntegrationTests(PipelineCheckHarness harness) => _harness = harness;

    [Fact]
    public async Task Replay_Of_A_Mapped_Document_Returns_The_Enriched_Pivot_With_Category_And_Rate()
    {
        // Document mappable (régime « NORMAL » → S 20 % sur la table validée) : le rejeu pose la catégorie/le taux
        // par ligne — exactement ce qu'un document Prêt-à-envoyer affiche dans le détail.
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        var replay = await _harness.ReplayContentAsync(documentId);

        replay.Available.Should().BeTrue("le pivot source stagé a été relu");
        var line = replay.MappedPivot!.Lines.Single();
        line.SourceRegimeCodes.Single().Should().Be("NORMAL", "le régime source lu est préservé");
        var tax = line.Taxes.Single();
        tax.CategoryCode.Should().Be(VatCategory.S, "le mapping validé pose la catégorie résultante (jamais inventée)");
        tax.Rate.Should().Be(20m, "le taux vient de la table validée (rejeu du MÊME moteur)");
        line.NetAmount.Should().Be(120.00m, "les montants source ne sont jamais recalculés (decimal préservé)");
    }

    [Fact]
    public async Task Replay_Of_A_Blocked_Document_Returns_The_Source_Pivot_With_Empty_Category()
    {
        // Document NON mappable (régime « INCONNU » absent de la table) : le rejeu BLOQUE → on expose le pivot
        // SOURCE tel que lu (régime « INCONNU » présent, catégorie/VATEX VIDES = diagnostic FACTUEL du blocage,
        // jamais une catégorie devinée — CLAUDE.md n°2). C'est ce qu'un document Bloqué affiche dans le détail.
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "INCONNU");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        var replay = await _harness.ReplayContentAsync(documentId);

        replay.Available.Should().BeTrue("le détail des lignes reste visible dès l'état Bloqué (diagnostic)");
        var line = replay.MappedPivot!.Lines.Single();
        line.SourceRegimeCodes.Single().Should().Be("INCONNU", "le régime source lu est restitué pour diagnostiquer le blocage");
        var tax = line.Taxes.Single();
        tax.CategoryCode.Should().BeNull("le mapping a bloqué : aucune catégorie n'est devinée (CLAUDE.md n°2)");
        tax.VatexCode.Should().BeNull("aucun VATEX n'est inventé sur un document bloqué");
    }

    [Fact]
    public async Task Replay_When_Staging_Is_Absent_Reports_Unavailable_For_Snapshot_Fallback()
    {
        // Document existant MAIS pivot source non stagé (purgé après émission, ou absent) : le rejeu n'a aucun
        // contenu à relire → « indisponible » (l'appelant retombe sur le snapshot transmis). On NE stage PAS.
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");
        var hash = PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));

        await _harness.SeedDetectedDocumentAsync(documentId, sourceReference, hash, pivot);

        var replay = await _harness.ReplayContentAsync(documentId);

        replay.Available.Should().BeFalse("staging absent → contenu indisponible (repli sur le snapshot transmis)");
        replay.MappedPivot.Should().BeNull();
    }

    [Fact]
    public async Task Replay_Of_An_Unknown_Document_Reports_Unavailable()
    {
        // Document inconnu du tenant : aucun contenu à relire → indisponible (la page rendra « introuvable »).
        var replay = await _harness.ReplayContentAsync(Guid.NewGuid());

        replay.Available.Should().BeFalse();
    }

    private async Task SeedAndStageAsync(Guid documentId, string sourceReference, PivotDocumentDto pivot)
    {
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);
        await _harness.SeedDetectedDocumentAsync(documentId, sourceReference, hash, pivot);
        await _harness.StagePayloadAsync(documentId, hash, json);
    }
}
