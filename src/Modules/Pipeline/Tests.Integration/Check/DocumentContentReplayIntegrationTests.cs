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
    public async Task Replay_Surfaces_The_Tenant_Default_Billing_Mentions_When_The_Document_Carries_None()
    {
        // BUG-26 (F12-A §3.4) : un document dont le pivot SOURCE ne porte ni termes de paiement ni notes voit ses
        // mentions de facturation EFFECTIVES résolues au read-time depuis le défaut TENANT (BT-20 + 3 notes légales
        // FR PMD/PMT/AAB), via la SOURCE UNIQUE d'injection (l'enricher) — exactement ce que la console affiche.
        await _harness.SeedBillingMentionsAsync(
            paymentTerms: "Paiement à 30 jours fin de mois.",
            latePenaltyTerms: "Pénalités de retard au taux légal.",
            recoveryFeeTerms: "Indemnité forfaitaire de recouvrement de 40 €.",
            discountTerms: "Pas d'escompte pour paiement anticipé.");

        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");
        pivot.PaymentTerms.Should().BeNull("le pivot source ne porte aucune mention (l'agent extrait des pièces, pas les CGV)");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        var replay = await _harness.ReplayContentAsync(documentId);

        replay.Available.Should().BeTrue();
        replay.PaymentTerms.Should().Be("Paiement à 30 jours fin de mois.", "le défaut tenant est injecté au read-time");
        replay.Notes.Should().HaveCount(3);
        replay.Notes.Should().ContainSingle(n => n.SubjectCode == "PMD" && n.Content == "Pénalités de retard au taux légal.");
        replay.Notes.Should().ContainSingle(n => n.SubjectCode == "PMT" && n.Content == "Indemnité forfaitaire de recouvrement de 40 €.");
        replay.Notes.Should().ContainSingle(n => n.SubjectCode == "AAB" && n.Content == "Pas d'escompte pour paiement anticipé.");

        await _harness.RemoveBillingMentionsAsync();
    }

    [Fact]
    public async Task Replay_Leaves_Mentions_Empty_When_No_Tenant_Mentions_Are_Configured()
    {
        // Aucune mention tenant configurée : le rejeu n'invente RIEN (CLAUDE.md n°2) — termes/notes restent vides.
        // On garantit l'état de départ (un autre test a pu seeder/retirer les mentions partagées).
        await _harness.RemoveBillingMentionsAsync();

        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        var replay = await _harness.ReplayContentAsync(documentId);

        replay.Available.Should().BeTrue();
        replay.PaymentTerms.Should().BeNull("aucune mention tenant → rien n'est injecté");
        replay.Notes.Should().BeEmpty();
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

    [Fact]
    public async Task Replay_Normalizes_The_Buyer_Country_Alias_To_Its_Iso_Code()
    {
        // ADR-0038 : le code pays acheteur arrive de la source sous une forme LEGACY non-ISO (« ENG ») ; la
        // plateforme le NORMALISE au READ-TIME depuis le référentiel cross-instance (alias ENG→GB seedé en
        // migration) — l'opérateur voit « GB ». Prouve le câblage RÉEL de bout en bout DocumentContentReplayService
        // → PivotCountryNormalizer → référentiel Postgres (via DI + base réelle) : supprimer un appel de câblage
        // ferait rougir ce test. Le pivot SOURCE (donc l'empreinte anti-doublon F06) reste inchangé : la
        // normalisation n'a lieu qu'au read-time (INV-REF-CTRY-02).
        var documentId = Guid.NewGuid();
        var sourceReference = "no_ba=" + documentId.ToString("N");
        var buyer = new PivotPartyDto(
            "Acheteur UK Ltd",
            siren: "945678902",
            address: new PivotAddressDto(city: "London", countryCode: "ENG"));
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL", customer: buyer);
        pivot.Customer!.Address!.CountryCode.Should().Be("ENG", "le pivot source porte le code legacy tel quel");

        await SeedAndStageAsync(documentId, sourceReference, pivot);

        var replay = await _harness.ReplayContentAsync(documentId);

        replay.Available.Should().BeTrue();
        replay.MappedPivot!.Customer!.Address!.CountryCode.Should().Be(
            "GB", "l'alias legacy ENG→GB (référentiel ADR-0038) est résolu au read-time via la base réelle");
    }

    private async Task SeedAndStageAsync(Guid documentId, string sourceReference, PivotDocumentDto pivot)
    {
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);
        await _harness.SeedDetectedDocumentAsync(documentId, sourceReference, hash, pivot);
        await _harness.StagePayloadAsync(documentId, hash, json);
    }
}
