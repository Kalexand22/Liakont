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
}
