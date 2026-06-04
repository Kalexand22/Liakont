namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class SourceTotalsRuleTests
{
    private readonly SourceTotalsRule _rule = new();

    [Fact]
    public async Task No_source_total_produces_no_issue()
    {
        var context = TestDoc.Context(totalGross: 1200m, sourceTotalGross: null);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_total_equal_produces_no_issue()
    {
        var context = TestDoc.Context(totalGross: 1200m, sourceTotalGross: 1200m);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_total_mismatch_is_warning_not_blocking()
    {
        var context = TestDoc.Context(totalGross: 1200m, sourceTotalGross: 1199.99m);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == SourceTotalsRule.MismatchCode)
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }
}
