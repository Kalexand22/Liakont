namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;
using static Liakont.Modules.Validation.Tests.Unit.PivotDocumentBuilder;

public sealed class VatexRequiredRuleTests
{
    private readonly VatexRequiredRule _rule = new();

    [Fact]
    public async Task Exonerated_line_without_vatex_is_blocking()
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: VatCategory.E, vatex: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(VatexRequiredRule.VatexMissingCode);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019"); // numéro de document cité
        issue.FieldRef.Should().Be("BT-121");
    }

    [Fact]
    public async Task Exonerated_line_with_vatex_present_is_ok()
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: VatCategory.E, vatex: "VATEX-EU-J") }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Exonerated_line_with_whitespace_vatex_is_blocking()
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: VatCategory.E, vatex: "   ") }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == VatexRequiredRule.VatexMissingCode);
    }

    [Fact]
    public async Task Exonerated_line_with_null_rate_still_requires_vatex()
    {
        // Taux absent pour une exonération = 0 → le motif VATEX reste obligatoire.
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: null, category: VatCategory.E, vatex: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == VatexRequiredRule.VatexMissingCode);
    }

    [Fact]
    public async Task Standard_line_without_vatex_is_ok()
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 20m, rate: 20m, category: VatCategory.S, vatex: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_offending_line_is_flagged()
    {
        var doc = Document(lines: new[]
        {
            Line(description: "Frais (S 20%)", taxes: new[] { Tax(taxAmount: 20m, rate: 20m, category: VatCategory.S) }),
            Line(description: "Adjudication marge (E 0%)", taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: VatCategory.E, vatex: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == VatexRequiredRule.VatexMissingCode);
    }

    [Fact]
    public async Task Document_with_no_line_produces_no_issue()
    {
        var issues = await _rule.ValidateAsync(Context(Document()));

        issues.Should().BeEmpty();
    }
}
