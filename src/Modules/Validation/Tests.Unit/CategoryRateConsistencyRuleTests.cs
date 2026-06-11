namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;
using static Liakont.Modules.Validation.Tests.Unit.PivotDocumentBuilder;

public sealed class CategoryRateConsistencyRuleTests
{
    private static readonly string[] RegimeX = { "REG-X" };

    private readonly CategoryRateConsistencyRule _rule = new();

    [Fact]
    public void Depends_on_tva_mapping_so_it_is_excluded_from_independent_aggregation()
    {
        // FIX06 : la cohérence catégorie/taux ne porte que sur la catégorie posée par l'enrichissement → exclue.
        _rule.DependsOnTvaMapping.Should().BeTrue();
    }

    [Theory]
    [InlineData(VatCategory.S)]
    [InlineData(VatCategory.AA)]
    [InlineData(VatCategory.AAA)]
    public async Task Positive_rate_category_with_zero_rate_is_blocking(VatCategory category)
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: category) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(CategoryRateConsistencyRule.CategoryRateInconsistentCode);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Theory]
    [InlineData(VatCategory.S)]
    [InlineData(VatCategory.AA)]
    [InlineData(VatCategory.AAA)]
    public async Task Positive_rate_category_with_null_rate_is_blocking(VatCategory category)
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 10m, rate: null, category: category) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CategoryRateConsistencyRule.CategoryRateInconsistentCode);
    }

    [Theory]
    [InlineData(VatCategory.S, 20)]
    [InlineData(VatCategory.AA, 10)]
    [InlineData(VatCategory.AAA, 2)]
    public async Task Positive_rate_category_with_positive_rate_is_ok(VatCategory category, int rate)
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 5m, rate: rate, category: category) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Reduced_rate_with_decimal_value_is_ok()
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 2.1m, rate: 2.1m, category: VatCategory.AAA) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData(VatCategory.E)]
    [InlineData(VatCategory.Z)]
    [InlineData(VatCategory.AE)]
    [InlineData(VatCategory.G)]
    [InlineData(VatCategory.K)]
    [InlineData(VatCategory.O)]
    public async Task Zero_rate_category_with_nonzero_rate_is_blocking(VatCategory category)
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 20m, rate: 20m, category: category) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CategoryRateConsistencyRule.CategoryRateInconsistentCode);
    }

    [Theory]
    [InlineData(VatCategory.E)]
    [InlineData(VatCategory.Z)]
    [InlineData(VatCategory.AE)]
    [InlineData(VatCategory.G)]
    [InlineData(VatCategory.K)]
    [InlineData(VatCategory.O)]
    public async Task Zero_rate_category_with_zero_rate_is_ok(VatCategory category)
    {
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: category) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData(VatCategory.E)]
    [InlineData(VatCategory.Z)]
    public async Task Zero_rate_category_with_null_rate_is_ok(VatCategory category)
    {
        // Taux absent = 0 pour une catégorie à taux zéro : pas d'incohérence.
        var doc = Document(lines: new[]
        {
            Line(taxes: new[] { Tax(taxAmount: 0m, rate: null, category: category) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Unresolved_category_is_skipped()
    {
        // Catégorie non résolue (régime non mappé) : hors périmètre de cette règle (→ MappingCoverageRule).
        var doc = Document(lines: new[]
        {
            Line(sourceRegimeCodes: RegimeX, taxes: new[] { Tax(taxAmount: 0m, rate: null, category: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_part_margin_document_is_ok()
    {
        // Modèle 2 lignes (F03 §2.3) : adjudication E 0% + frais S 20% → cohérent.
        var doc = Document(lines: new[]
        {
            Line(description: "Adjudication marge", taxes: new[] { Tax(taxAmount: 0m, rate: 0m, category: VatCategory.E, vatex: "VATEX-EU-J") }),
            Line(description: "Frais acheteur", taxes: new[] { Tax(taxAmount: 20m, rate: 20m, category: VatCategory.S) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }
}
