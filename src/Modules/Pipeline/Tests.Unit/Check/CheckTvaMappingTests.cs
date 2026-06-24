namespace Liakont.Modules.Pipeline.Tests.Unit.Check;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Xunit;

/// <summary>Logique pure du mapping/enrichissement TVA au CHECK (INV-PIPELINE-003/008).</summary>
public sealed class CheckTvaMappingTests
{
    [Fact]
    public void BuildPlan_SingleRegime_SingleTax_Produces_One_Request_With_Part_Autre()
    {
        var pivot = CheckTestData.SingleLinePivot(regimeCode: "NORMAL");

        var plan = CheckTvaMapping.BuildPlan(pivot);

        plan.Requests.Should().HaveCount(1);
        plan.Requests[0].SourceRegimeCode.Should().Be("NORMAL");
        plan.Requests[0].Part.Should().Be(TvaMappingPart.Autre, "le pipeline générique ne devine jamais adjudication/frais (ADR-0004/F03 §2.3)");
        plan.Requests[0].SourceFlags.Should().BeNull();
        plan.RequestLineIndexes.Should().Equal(0);
        plan.ShapeBlockReasons.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlan_Line_With_Multiple_Regime_Codes_Is_Blocked_Not_Mapped()
    {
        var regimes = new[] { "A", "B" };
        var line = new PivotLineDto(
            description: "Ligne ambiguë",
            netAmount: 100m,
            sourceRegimeCodes: regimes,
            taxes: new[] { new PivotLineTaxDto(20m, 20m) });
        var pivot = CheckTestData.BuildPivot(new[] { line }, 100m, 20m);

        var plan = CheckTvaMapping.BuildPlan(pivot);

        plan.Requests.Should().BeEmpty("aucune association régime → ventilation n'est devinée (CLAUDE.md n°2)");
        plan.ShapeBlockReasons.Should().HaveCount(1);
        plan.ShapeBlockReasons[0].Should().Contain("2 code(s) régime");
    }

    [Fact]
    public void BuildPlan_Line_With_Multiple_Taxes_Is_Blocked_Not_Mapped()
    {
        var regimes = new[] { "NORMAL" };
        var line = new PivotLineDto(
            description: "Ligne deux taxes",
            netAmount: 100m,
            sourceRegimeCodes: regimes,
            taxes: new[] { new PivotLineTaxDto(20m, 20m), new PivotLineTaxDto(5m, 5m) });
        var pivot = CheckTestData.BuildPivot(new[] { line }, 100m, 25m);

        var plan = CheckTvaMapping.BuildPlan(pivot);

        plan.Requests.Should().BeEmpty();
        plan.ShapeBlockReasons.Should().ContainSingle().Which.Should().Contain("2 ventilation(s)");
    }

    [Fact]
    public void Evaluate_All_Mapped_Enriches_Category_And_Vatex_And_Keeps_Source_Amounts()
    {
        var pivot = CheckTestData.SingleLinePivot(regimeCode: "NORMAL", net: 120.00m, tax: 24.00m, rate: 20m);
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = CheckTestData.MappedResult(version: "cmp-v1");

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);

        evaluation.IsBlocked.Should().BeFalse();
        evaluation.MappingVersion.Should().Be("cmp-v1");
        evaluation.EnrichedDocument.Should().NotBeNull();

        var enrichedTax = evaluation.EnrichedDocument!.Lines[0].Taxes[0];
        enrichedTax.CategoryCode.Should().Be(VatCategory.S);
        enrichedTax.VatexCode.Should().BeNull();
        enrichedTax.TaxAmount.Should().Be(24.00m, "le CHECK ne recalcule jamais les montants source");
        enrichedTax.Rate.Should().Be(20m);
    }

    [Fact]
    public void Evaluate_Unmapped_Line_Returns_Blocked_With_Engine_Reason()
    {
        var pivot = CheckTestData.SingleLinePivot(regimeCode: "INCONNU");
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = CheckTestData.BlockedLineResult();

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);

        evaluation.IsBlocked.Should().BeTrue();
        evaluation.BlockReason.Should().Contain("absent de la table de mapping");
        evaluation.EnrichedDocument.Should().BeNull();
    }

    [Fact]
    public void Evaluate_Mismatched_Result_Count_Throws()
    {
        var pivot = CheckTestData.SingleLinePivot();
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = new DocumentTvaMappingResult
        {
            TableExists = true,
            IsValidated = true,
            MappingVersion = "cmp-v1",
            Lines = Array.Empty<TvaLineMappingResult>(),
        };

        var act = () => CheckTvaMapping.Evaluate(pivot, plan, mapping);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Evaluate_MarginDocument_Derives_B2cReportingDeclaration_Marker()
    {
        // Adjudication exonérée mappée E + VATEX-EU-J (régime marge, table validée), frais acheteur, acheteur
        // particulier, aucune TVA distincte (297 E) → le pivot enrichi porte le marqueur de déclaration de marge.
        var regimes = new[] { "MARGE" };
        var line = new PivotLineDto(
            description: "Adjudication lot 1",
            netAmount: 2000m,
            sourceRegimeCodes: regimes,
            taxes: new[] { new PivotLineTaxDto(taxAmount: 0m, rate: 0m) });
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: "100022",
            issueDate: new DateTime(2024, 1, 12),
            sourceReference: "encheresv6:ba:100022",
            supplier: null,
            totals: new PivotTotalsDto(2000m, 0m, 2000m),
            operationCategory: null,
            customer: new PivotPartyDto("Acheteur Particulier"),
            lines: new[] { line },
            buyerFees: new[] { new PivotBuyerFeeDto("100022", 401.28m, sourceRegimeCode: "MARGE") });
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = MarginMappedResult();

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);

        evaluation.IsBlocked.Should().BeFalse();
        evaluation.EnrichedDocument!.IsB2cReportingDeclaration.Should().BeTrue(
            "régime marge (E + VATEX-EU-J) + frais + acheteur B2C + 297 E → déclaration de marge B2C dérivée (F03)");
    }

    [Fact]
    public void Evaluate_TaxableDocument_Does_Not_Derive_Marker()
    {
        // Document taxable (S, TVA distincte) : jamais une déclaration de marge → marqueur non posé.
        var pivot = CheckTestData.SingleLinePivot(regimeCode: "NORMAL", net: 120.00m, tax: 24.00m, rate: 20m);
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = CheckTestData.MappedResult(version: "cmp-v1");

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);

        evaluation.EnrichedDocument!.IsB2cReportingDeclaration.Should().BeFalse(
            "un document taxable n'est jamais marqué déclaration de marge B2C");
    }

    [Fact]
    public void Evaluate_Preserves_PaymentDueDate_After_Enrichment()
    {
        var dueDate = new DateTime(2026, 2, 15);
        var pivot = CheckTestData.SingleLinePivot(regimeCode: "NORMAL", net: 120.00m, tax: 24.00m, rate: 20m);

        // Rebuild the pivot with a PaymentDueDate set (last optional ctor arg).
        pivot = new PivotDocumentDto(
            sourceDocumentKind: pivot.SourceDocumentKind,
            number: pivot.Number,
            issueDate: pivot.IssueDate,
            sourceReference: pivot.SourceReference,
            supplier: pivot.Supplier,
            totals: pivot.Totals,
            operationCategory: pivot.OperationCategory,
            currencyCode: pivot.CurrencyCode,
            customer: pivot.Customer,
            lines: pivot.Lines,
            paymentDueDate: dueDate);

        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = CheckTestData.MappedResult(version: "cmp-v1");

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);

        evaluation.IsBlocked.Should().BeFalse();
        evaluation.EnrichedDocument.Should().NotBeNull();
        evaluation.EnrichedDocument!.PaymentDueDate.Should().Be(dueDate);
    }

    private static DocumentTvaMappingResult MarginMappedResult() =>
        new()
        {
            TableExists = true,
            IsValidated = true,
            MappingVersion = "cmp-v1",
            Lines = new[]
            {
                new TvaLineMappingResult
                {
                    SourceRegimeCode = "MARGE",
                    LineRef = "0",
                    IsMapped = true,
                    Category = "E",
                    Rate = 0m,
                    Vatex = "VATEX-EU-J",
                },
            },
        };
}
