namespace Liakont.Host.Tests.Unit.Documents;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Host.Documents;
using Xunit;

// FIX205 — projection du pivot TRANSMIS (snapshot canonique ADR-0007) en lignes affichables. On prouve le
// chemin réel : un pivot sérialisé par le writer canonique du contrat → relu → projeté, catégorie/VATEX
// résolus en français (F03 §2.1), montants decimal préservés. Et la robustesse : un snapshot absent ou
// illisible NE casse PAS la lecture (retombe sur une liste vide → la vue affiche sa note).
public sealed class DocumentLineProjectionTests
{
    // Régimes source extraits en champs (CA1861 : pas de tableau constant en argument).
    private static readonly string[] StdRegime = ["FR-STD"];
    private static readonly string[] RedRegime = ["FR-RED"];
    private static readonly string[] MultiRegime = ["FR-A", "FR-B"];

    [Fact]
    public void FromTransmittedSnapshot_Projects_Lines_With_Resolved_Mapping()
    {
        var json = CanonicalJson.Serialize(BuildPivot());

        var lines = DocumentLineProjection.FromTransmittedSnapshot(json);

        lines.Should().HaveCount(2);

        var first = lines[0];
        first.Label.Should().Be("Vente principale");
        first.Quantity.Should().Be(2m);
        first.NetAmount.Should().Be(900m);
        first.SourceRegime.Should().Be("FR-STD");
        first.Category.Should().Be("S — Taux normal");
        first.Vatex.Should().Be("—");
        first.TaxAmount.Should().Be(150m);
        first.Rate.Should().Be(20m);

        var second = lines[1];
        second.Label.Should().Be("Frais de port");
        second.NetAmount.Should().Be(100m);
        second.SourceRegime.Should().Be("FR-RED");
        second.Category.Should().Be("AA — Taux réduit");
        second.TaxAmount.Should().Be(12.80m);
        second.Rate.Should().Be(10m);
    }

    [Fact]
    public void FromTransmittedSnapshot_Joins_Multiple_Source_Regimes()
    {
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "2026-020",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "src/2026-020",
            supplier: new PivotPartyDto(name: "Vendeur SARL", siren: "123456782"),
            totals: new PivotTotalsDto(totalNet: 500m, totalTax: 100m, totalGross: 600m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[]
            {
                new PivotLineDto(
                    description: "Ligne multi-régime",
                    netAmount: 500m,
                    sourceRegimeCodes: MultiRegime,
                    taxes: new[] { new PivotLineTaxDto(taxAmount: 100m, rate: 20m, categoryCode: VatCategory.S) }),
            });

        var lines = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(pivot));

        lines.Should().ContainSingle();
        lines[0].SourceRegime.Should().Be("FR-A, FR-B");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromTransmittedSnapshot_Returns_Empty_When_No_Snapshot(string? snapshot)
    {
        DocumentLineProjection.FromTransmittedSnapshot(snapshot).Should().BeEmpty();
    }

    [Theory]
    [InlineData("{ not json")] // JSON invalide
    [InlineData("{}")] // JSON valide mais pivot incomplet (propriétés obligatoires absentes)
    [InlineData("[]")] // mauvaise forme
    public void FromTransmittedSnapshot_Returns_Empty_On_Unreadable_Snapshot(string snapshot)
    {
        // Un snapshot transmis illisible ne doit JAMAIS casser la lecture du détail (le pivot intègre reste
        // dans le coffre WORM, récupérable via l'export). On retombe proprement sur une liste vide.
        DocumentLineProjection.FromTransmittedSnapshot(snapshot).Should().BeEmpty();
    }

    private static PivotDocumentDto BuildPivot() => new(
        sourceDocumentKind: "invoice",
        number: "2026-010",
        issueDate: new DateTime(2026, 6, 1),
        sourceReference: "src/2026-010",
        supplier: new PivotPartyDto(name: "Vendeur SARL", siren: "123456782"),
        totals: new PivotTotalsDto(totalNet: 1000m, totalTax: 162.80m, totalGross: 1162.80m),
        operationCategory: OperationCategory.LivraisonBiens,
        currencyCode: "EUR",
        lines: new[]
        {
            new PivotLineDto(
                description: "Vente principale",
                netAmount: 900m,
                quantity: 2m,
                sourceRegimeCodes: StdRegime,
                taxes: new[] { new PivotLineTaxDto(taxAmount: 150m, rate: 20m, categoryCode: VatCategory.S) }),
            new PivotLineDto(
                description: "Frais de port",
                netAmount: 100m,
                sourceRegimeCodes: RedRegime,
                taxes: new[] { new PivotLineTaxDto(taxAmount: 12.80m, rate: 10m, categoryCode: VatCategory.AA) }),
        });
}
