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

    [Fact]
    public async Task Document_charge_is_included_in_net_reconciliation()
    {
        // Σ lignes HT 1000 + charge 50 = 1050 = Total HT : cohérent (BR-CO-13).
        var context = TestDoc.Context(
            totalNet: 1050m,
            totalTax: 200m,
            totalGross: 1250m,
            lines: new[] { TestDoc.Line(1000m, 200m) },
            charges: new[] { TestDoc.Charge(50m, isCharge: true) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().NotContain(i => i.Code == LineTotalsRule.NetMismatchCode);
    }

    [Fact]
    public async Task Document_allowance_is_included_in_net_reconciliation()
    {
        // Σ lignes HT 1000 − remise 100 = 900 = Total HT : cohérent (BR-CO-13).
        var context = TestDoc.Context(
            totalNet: 900m,
            totalTax: 200m,
            totalGross: 1100m,
            lines: new[] { TestDoc.Line(1000m, 200m) },
            charges: new[] { TestDoc.Charge(100m, isCharge: false) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().NotContain(i => i.Code == LineTotalsRule.NetMismatchCode);
    }

    [Fact]
    public async Task Net_mismatch_with_document_charge_still_blocks()
    {
        // Σ lignes 1000 + charge 50 = 1050 ≠ Total HT 1000 : BLOQUANT (la réconciliation reste stricte).
        var context = TestDoc.Context(
            totalNet: 1000m,
            totalTax: 200m,
            totalGross: 1200m,
            lines: new[] { TestDoc.Line(1000m, 200m) },
            charges: new[] { TestDoc.Charge(50m, isCharge: true) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == LineTotalsRule.NetMismatchCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Tax_reconciliation_is_skipped_when_document_charges_present()
    {
        // TVA lignes 200 ≠ Total TVA 999, mais une charge document est présente : le contrôle TVA est
        // court-circuité (TVA des charges non résolue en VAL03) -> pas de faux positif.
        var context = TestDoc.Context(
            totalNet: 1050m,
            totalTax: 999m,
            totalGross: 2049m,
            lines: new[] { TestDoc.Line(1000m, 200m) },
            charges: new[] { TestDoc.Charge(50m, isCharge: true) });

        var issues = await _rule.ValidateAsync(context);

        issues.Should().NotContain(i => i.Code == LineTotalsRule.TaxMismatchCode);
    }
}
