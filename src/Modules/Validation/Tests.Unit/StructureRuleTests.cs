namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class StructureRuleTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly StructureRule _rule = new(new FixedTimeProvider(Now));

    [Fact]
    public async Task Coherent_document_produces_no_issue()
    {
        var context = TestDoc.Context(issueDate: new DateTime(2024, 1, 15), currencyCode: "EUR");

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_without_lines_is_blocking()
    {
        var context = TestDoc.Context(lines: Array.Empty<PivotLineDto>());

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == StructureRule.NoLinesCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Future_issue_date_is_blocking()
    {
        var context = TestDoc.Context(issueDate: new DateTime(2030, 1, 1));

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == StructureRule.DateInFutureCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Issue_date_today_is_not_in_the_future()
    {
        var context = TestDoc.Context(issueDate: Now.UtcDateTime.Date);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().NotContain(i => i.Code == StructureRule.DateInFutureCode);
    }

    [Fact]
    public async Task Very_old_issue_date_is_a_warning()
    {
        var context = TestDoc.Context(issueDate: new DateTime(1999, 12, 31));

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == StructureRule.DateTooOldCode)
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Invalid_currency_is_blocking()
    {
        var context = TestDoc.Context(currencyCode: "XYZ");

        var issues = await _rule.ValidateAsync(context);

        issues.Should().ContainSingle(i => i.Code == StructureRule.CurrencyInvalidCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Empty_currency_is_blocking()
    {
        var context = TestDoc.Context(currencyCode: string.Empty);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().Contain(i => i.Code == StructureRule.CurrencyInvalidCode);
    }

    [Fact]
    public async Task Lowercase_currency_is_accepted()
    {
        var context = TestDoc.Context(currencyCode: "eur");

        var issues = await _rule.ValidateAsync(context);

        issues.Should().NotContain(i => i.Code == StructureRule.CurrencyInvalidCode);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
