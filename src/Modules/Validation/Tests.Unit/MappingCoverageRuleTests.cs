namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;
using static Liakont.Modules.Validation.Tests.Unit.PivotDocumentBuilder;

public sealed class MappingCoverageRuleTests
{
    private static readonly string[] RegimeStd = { "REG-STD" };
    private static readonly string[] Regime12 = { "REG-12" };
    private static readonly string[] RegimeAb = { "REG-A", "REG-B" };
    private static readonly string[] Regime1 = { "REG-1" };
    private static readonly string[] Regime2 = { "REG-2" };

    private readonly MappingCoverageRule _rule = new();

    [Fact]
    public void Depends_on_tva_mapping_so_it_is_excluded_from_independent_aggregation()
    {
        // FIX06 : elle CONSTATE l'absence de catégorie résolue ; sur un document non mappé elle ferait doublon
        // avec le blocage de mapping → exclue de l'agrégation indépendante.
        _rule.DependsOnTvaMapping.Should().BeTrue();
    }

    [Fact]
    public async Task Line_with_regime_and_resolved_category_is_ok()
    {
        var doc = Document(lines: new[]
        {
            Line(sourceRegimeCodes: RegimeStd, taxes: new[] { Tax(category: VatCategory.S) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Line_with_regime_but_no_tax_is_blocking()
    {
        var doc = Document(lines: new[]
        {
            Line(sourceRegimeCodes: Regime12, taxes: null),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(MappingCoverageRule.MappingCoverageMissingCode);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("REG-12"); // code régime non mappé cité
        issue.MessageOperateur.Should().Contain("2019");
        issue.FieldRef.Should().Be("BT-151");
    }

    [Fact]
    public async Task Line_with_regime_but_null_category_is_blocking()
    {
        var doc = Document(lines: new[]
        {
            Line(sourceRegimeCodes: Regime12, taxes: new[] { Tax(taxAmount: 0m, rate: null, category: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == MappingCoverageRule.MappingCoverageMissingCode);
    }

    [Fact]
    public async Task Line_without_source_regime_is_out_of_scope()
    {
        // Pas de régime source déclaré : ce n'est pas un problème de COUVERTURE du mapping.
        var doc = Document(lines: new[]
        {
            Line(sourceRegimeCodes: null, taxes: new[] { Tax(taxAmount: 0m, rate: null, category: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Line_with_one_resolved_and_one_unresolved_tax_is_blocking()
    {
        var doc = Document(lines: new[]
        {
            Line(
                sourceRegimeCodes: RegimeAb,
                taxes: new[]
                {
                    Tax(taxAmount: 20m, rate: 20m, category: VatCategory.S),
                    Tax(taxAmount: 0m, rate: null, category: null),
                }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == MappingCoverageRule.MappingCoverageMissingCode);
    }

    [Fact]
    public async Task Fully_unmapped_document_blocks_each_regime_line_failsafe()
    {
        // Filet de sécurité : un document non mappé (toutes catégories nulles) est bloqué, jamais envoyé.
        var doc = Document(lines: new[]
        {
            Line(description: "Ligne 1", sourceRegimeCodes: Regime1, taxes: new[] { Tax(taxAmount: 0m, rate: null, category: null) }),
            Line(description: "Ligne 2", sourceRegimeCodes: Regime2, taxes: new[] { Tax(taxAmount: 0m, rate: null, category: null) }),
        });

        var issues = await _rule.ValidateAsync(Context(doc));

        issues.Should().HaveCount(2);
        issues.Should().OnlyContain(i => i.Code == MappingCoverageRule.MappingCoverageMissingCode);
    }
}
