namespace Liakont.Host.Tests.Unit.Documents;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Host.Documents;
using Xunit;

// FIX205 — projection du pivot TRANSMIS (snapshot canonique ADR-0007) en contenu affichable (lignes + charges
// document + contrôle de cohérence). On prouve le chemin réel : un pivot sérialisé par le writer canonique du
// contrat → relu → projeté, catégorie/VATEX résolus en français (F03 §2.1), montants decimal préservés ; et la
// cohérence MIROIR de LineTotalsRule (net AVEC charges, TVA seulement sans charge globale). Robustesse : un
// snapshot absent ou illisible NE casse PAS la lecture (contenu vide → la vue affiche sa note).
public sealed class DocumentLineProjectionTests
{
    // Régimes source extraits en champs (CA1861 : pas de tableau constant en argument).
    private static readonly string[] StdRegime = ["FR-STD"];
    private static readonly string[] RedRegime = ["FR-RED"];
    private static readonly string[] MultiRegime = ["FR-A", "FR-B"];

    [Fact]
    public void FromTransmittedSnapshot_Projects_Lines_With_Resolved_Mapping()
    {
        var content = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(BuildPivot()));

        content.Lines.Should().HaveCount(2);
        content.Charges.Should().BeEmpty();

        var first = content.Lines[0];
        first.Label.Should().Be("Vente principale");
        first.Quantity.Should().Be(2m);
        first.NetAmount.Should().Be(900m);
        first.SourceRegime.Should().Be("FR-STD");
        first.Category.Should().Be("S — Taux normal");
        first.Vatex.Should().Be("—");
        first.TaxAmount.Should().Be(150m);
        first.Rate.Should().Be(20m);

        var second = content.Lines[1];
        second.Label.Should().Be("Frais de port");
        second.NetAmount.Should().Be(100m);
        second.SourceRegime.Should().Be("FR-RED");
        second.Category.Should().Be("AA — Taux réduit");
        second.TaxAmount.Should().Be(12.80m);
        second.Rate.Should().Be(10m);
    }

    [Fact]
    public void FromTransmittedSnapshot_Reconciles_Net_And_Tax_When_No_Document_Charge()
    {
        // BuildPivot : Σ lignes HT 900 + 100 = 1000 = TotalNet ; Σ TVA 150 + 12,80 = 162,80 = TotalTax ; pas de charge.
        var totals = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(BuildPivot())).Totals;

        totals.Should().NotBeNull();
        totals!.NetConsistent.Should().BeTrue();
        totals.TaxChecked.Should().BeTrue("aucune charge/remise de niveau document");
        totals.TaxConsistent.Should().BeTrue();
        totals.Consistent.Should().BeTrue();
        totals.ExpectedNet.Should().Be(1000m);
        totals.DocumentNet.Should().Be(1000m);
    }

    [Fact]
    public void FromTransmittedSnapshot_Includes_Document_Charge_In_Net_Reconciliation_And_Skips_Tax()
    {
        // Σ lignes HT 900 + charge 100 = 1000 = TotalNet → net cohérent MALGRÉ la charge (mirroir BR-CO-13).
        // Avec une charge globale, la TVA n'est pas réconciliée (sa TVA n'est pas résolue à ce stade) : pas de faux écart.
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "2026-030",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "src/2026-030",
            supplier: new PivotPartyDto(name: "Vendeur SARL", siren: "123456782"),
            totals: new PivotTotalsDto(totalNet: 1000m, totalTax: 180m, totalGross: 1180m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[]
            {
                new PivotLineDto(
                    description: "Vente",
                    netAmount: 900m,
                    sourceRegimeCodes: StdRegime,
                    taxes: new[] { new PivotLineTaxDto(taxAmount: 180m, rate: 20m, categoryCode: VatCategory.S) }),
            },
            documentCharges: new[] { new PivotDocumentChargeDto(isCharge: true, amount: 100m, reason: "éco-contribution") });

        var content = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(pivot));

        content.Charges.Should().ContainSingle();
        content.Charges[0].IsCharge.Should().BeTrue();
        content.Charges[0].Amount.Should().Be(100m);
        content.Charges[0].Label.Should().Be("éco-contribution");

        content.Totals.Should().NotBeNull();
        content.Totals!.NetConsistent.Should().BeTrue("900 lignes + 100 charge = 1000 = TotalNet");
        content.Totals.TaxChecked.Should().BeFalse("la TVA d'une charge globale n'est pas résolue à ce stade");
        content.Totals.Consistent.Should().BeTrue();
    }

    [Fact]
    public void FromTransmittedSnapshot_Flags_Net_Mismatch_When_Lines_Do_Not_Sum_To_Total()
    {
        // Pivot incohérent (TotalNet 1000 mais une seule ligne à 500, aucune charge) → écart net signalé.
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "2026-031",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "src/2026-031",
            supplier: new PivotPartyDto(name: "Vendeur SARL", siren: "123456782"),
            totals: new PivotTotalsDto(totalNet: 1000m, totalTax: 100m, totalGross: 1100m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[]
            {
                new PivotLineDto(
                    description: "Ligne unique",
                    netAmount: 500m,
                    sourceRegimeCodes: StdRegime,
                    taxes: new[] { new PivotLineTaxDto(taxAmount: 100m, rate: 20m, categoryCode: VatCategory.S) }),
            });

        var totals = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(pivot)).Totals;

        totals.Should().NotBeNull();
        totals!.NetConsistent.Should().BeFalse();
        totals.Consistent.Should().BeFalse();
        totals.ExpectedNet.Should().Be(500m);
        totals.DocumentNet.Should().Be(1000m);
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

        var content = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(pivot));

        content.Lines.Should().ContainSingle();
        content.Lines[0].SourceRegime.Should().Be("FR-A, FR-B");
    }

    [Fact]
    public void FromTransmittedSnapshot_Renders_The_Vatex_Exemption_Code()
    {
        // VATEX (BT-121) = motif d'exonération : son rendu non vide doit être exercé (catégorie E + code VATEX).
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "2026-021",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "src/2026-021",
            supplier: new PivotPartyDto(name: "Vendeur SARL", siren: "123456782"),
            totals: new PivotTotalsDto(totalNet: 500m, totalTax: 0m, totalGross: 500m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[]
            {
                new PivotLineDto(
                    description: "Prestation exonérée",
                    netAmount: 500m,
                    sourceRegimeCodes: StdRegime,
                    taxes: new[] { new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-AE") }),
            });

        var content = DocumentLineProjection.FromTransmittedSnapshot(CanonicalJson.Serialize(pivot));

        content.Lines.Should().ContainSingle();
        content.Lines[0].Category.Should().Be("E — Exonéré (motif VATEX requis)");
        content.Lines[0].Vatex.Should().Be("VATEX-EU-AE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromTransmittedSnapshot_Returns_Empty_When_No_Snapshot(string? snapshot)
    {
        var content = DocumentLineProjection.FromTransmittedSnapshot(snapshot);
        content.Lines.Should().BeEmpty();
        content.Charges.Should().BeEmpty();
        content.Totals.Should().BeNull();
        content.HasLines.Should().BeFalse();
    }

    [Theory]
    [InlineData("{ not json")] // JSON invalide
    [InlineData("{}")] // JSON valide mais pivot incomplet (propriétés obligatoires absentes)
    [InlineData("[]")] // mauvaise forme
    public void FromTransmittedSnapshot_Returns_Empty_On_Unreadable_Snapshot(string snapshot)
    {
        // Un snapshot transmis illisible ne doit JAMAIS casser la lecture du détail (le pivot intègre reste
        // dans le coffre WORM, récupérable via l'export). On retombe proprement sur un contenu vide.
        var content = DocumentLineProjection.FromTransmittedSnapshot(snapshot);
        content.Lines.Should().BeEmpty();
        content.Totals.Should().BeNull();
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
