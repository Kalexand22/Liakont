namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class ArithmeticRuleTests
{
    private readonly ArithmeticRule _rule = new();

    [Fact]
    public async Task Coherent_totals_produce_no_issue()
    {
        var context = TestDoc.Context(totalNet: 1000m, totalTax: 200m, totalGross: 1200m);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Gross_not_equal_to_net_plus_tax_is_blocking()
    {
        var context = TestDoc.Context(totalNet: 1000m, totalTax: 200m, totalGross: 1300m);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == ArithmeticRule.MismatchCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Rounding_gap_of_one_cent_is_blocking_no_tolerance()
    {
        // 100.00 + 20.00 = 120.00 ≠ 120.01 : BR-CO-15 sans tolérance (EN 16931).
        var context = TestDoc.Context(totalNet: 100m, totalTax: 20m, totalGross: 120.01m);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().Contain(i => i.Code == ArithmeticRule.MismatchCode);
    }
}
