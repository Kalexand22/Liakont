namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class LineTotalsRuleTests
{
    private readonly LineTotalsRule _rule = new();

    [Fact]
    public async Task Coherent_document_produces_no_issue()
    {
        var context = TestDoc.Context(
            totalNet: 1000m,
            totalTax: 200m,
            totalGross: 1200m,
            lines: new[] { TestDoc.Line(600m, 120m), TestDoc.Line(400m, 80m) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Net_total_mismatch_is_blocking()
    {
        var context = TestDoc.Context(
            totalNet: 1000m,
            totalTax: 200m,
            totalGross: 1200m,
            lines: new[] { TestDoc.Line(900m, 200m) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == LineTotalsRule.NetMismatchCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Tax_total_mismatch_is_blocking()
    {
        var context = TestDoc.Context(
            totalNet: 1000m,
            totalTax: 200m,
            totalGross: 1200m,
            lines: new[] { TestDoc.Line(1000m, 150m) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == LineTotalsRule.TaxMismatchCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Rounding_gap_of_one_cent_is_blocking_no_tolerance()
    {
        // Σ lignes HT = 1160.00 mais total document = 1160.01 : aucun rattrapage (EN 16931, tolérance 0).
        var context = TestDoc.Context(
            totalNet: 1160.01m,
            totalTax: 0m,
            totalGross: 1160.01m,
            lines: new[] { TestDoc.Line(1160.00m, 0m, rate: 0m) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().Contain(i => i.Code == LineTotalsRule.NetMismatchCode);
    }

    [Fact]
    public async Task Half_up_rounding_is_applied_to_line_sum()
    {
        // Σ lignes HT = 1160.005 -> arrondi half-up = 1160.01 = total document : cohérent.
        var context = TestDoc.Context(
            totalNet: 1160.01m,
            totalTax: 0m,
            totalGross: 1160.01m,
            lines: new[] { TestDoc.Line(1160.005m, 0m, rate: 0m) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_without_lines_is_ignored_by_this_rule()
    {
        var context = TestDoc.Context(
            totalNet: 1000m,
            totalTax: 200m,
            totalGross: 1200m,
            lines: Array.Empty<PivotLineDto>());

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }
}
