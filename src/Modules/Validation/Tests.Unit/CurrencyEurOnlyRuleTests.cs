namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class CurrencyEurOnlyRuleTests
{
    private readonly CurrencyEurOnlyRule _rule = new();

    [Fact]
    public async Task Euro_document_produces_no_issue()
    {
        var context = TestDoc.Context(currencyCode: "EUR");

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Lowercase_euro_is_accepted()
    {
        var context = TestDoc.Context(currencyCode: "eur");

        var issues = await _rule.ValidateAsync(context);

        issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("CHF")]
    public async Task Foreign_currency_is_blocking(string currencyCode)
    {
        var context = TestDoc.Context(currencyCode: currencyCode);

        var issues = await _rule.ValidateAsync(context);

        var issue = issues.Should().ContainSingle(i => i.Code == CurrencyEurOnlyRule.NonEurCurrencyCode).Subject;
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain(currencyCode);

        // Message opérateur FR actionnable, citant le numéro de document (CLAUDE.md n°12).
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    public async Task Invalid_or_empty_currency_is_left_to_StructureRule(string currencyCode)
    {
        // Un code absent/invalide n'est PAS du ressort de ce verrou EUR-only : StructureRule le bloque
        // déjà (DOC_CURRENCY_INVALID). Cette règle ne doit pas produire de double signal bloquant.
        var context = TestDoc.Context(currencyCode: currencyCode);

        var issues = await _rule.ValidateAsync(context);

        issues.Should().NotContain(i => i.Code == CurrencyEurOnlyRule.NonEurCurrencyCode);
    }
}
