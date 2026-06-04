namespace Liakont.Modules.TvaMapping.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Mutations de la table de mapping TVA (item TVA05 §1/§2/§4) : ajout/modification/suppression de règle
/// et validation humaine. Vérifie l'invalidation systématique de la validation après une mutation, le
/// rejet d'une mutation invalide SANS effet de bord (agrégat inchangé), et la résolution de règle.
/// </summary>
public sealed class MappingTableMutationTests
{
    private static MappingRule FixedRule(
        string code,
        VatCategory category,
        decimal rate,
        MappingPart part = MappingPart.Adjudication,
        string? vatex = null) => new()
        {
            SourceRegimeCode = code,
            Part = part,
            Category = category,
            Vatex = vatex,
            RateMode = RateMode.Fixed,
            RateValue = rate,
        };

    private static MappingTable ValidatedTable(params MappingRule[] rules)
        => MappingTable.Create(
            Guid.NewGuid(), "cmp-v1", "Expert-comptable", new DateOnly(2026, 7, 15),
            MappingDefaultBehavior.Block, rules);

    [Fact]
    public void AddRule_Appends_And_Invalidates()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));
        table.IsValidated.Should().BeTrue();

        table.AddRule(FixedRule("REGIME-B", VatCategory.AA, 10m));

        table.Rules.Should().HaveCount(2);
        table.Rules[1].SourceRegimeCode.Should().Be("REGIME-B");
        table.IsValidated.Should().BeFalse("toute mutation repasse la table « NON VALIDÉE » (item TVA05 §2).");
        table.ValidatedBy.Should().BeNull();
        table.ValidatedDate.Should().BeNull();
        table.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void AddRule_Duplicate_Throws_And_Leaves_Table_Unchanged()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));

        var act = () => table.AddRule(FixedRule("REGIME-A", VatCategory.AA, 10m));

        act.Should().Throw<InvalidMappingTableException>();
        table.Rules.Should().HaveCount(1, "une mutation rejetée n'a aucun effet de bord sur l'agrégat.");
        table.IsValidated.Should().BeTrue("la validation n'est pas effacée si la mutation échoue.");
    }

    [Fact]
    public void AddRule_E_Without_Vatex_Throws_And_Leaves_Table_Unchanged()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));

        var act = () => table.AddRule(FixedRule("REGIME-X", VatCategory.E, 0m, vatex: null));

        act.Should().Throw<InvalidMappingTableException>()
            .Which.Violations.Should().Contain(v => v.Contains("VATEX"));
        table.Rules.Should().HaveCount(1);
        table.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void UpdateRule_Replaces_Returns_Previous_And_Invalidates()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));

        var previous = table.UpdateRule(
            "REGIME-A", MappingPart.Adjudication, FixedRule("REGIME-A", VatCategory.AA, 10m));

        previous.Category.Should().Be(VatCategory.S);
        previous.RateValue.Should().Be(20m);
        table.Rules.Should().ContainSingle();
        table.Rules[0].Category.Should().Be(VatCategory.AA);
        table.Rules[0].RateValue.Should().Be(10m);
        table.IsValidated.Should().BeFalse();
    }

    [Fact]
    public void UpdateRule_Not_Found_Throws_NotFound()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));

        var act = () => table.UpdateRule(
            "REGIME-ABSENT", MappingPart.Adjudication, FixedRule("REGIME-ABSENT", VatCategory.S, 20m));

        act.Should().Throw<NotFoundException>();
        table.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void UpdateRule_To_Invalid_Throws_And_Leaves_Table_Unchanged()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));

        // E à 0 % sans VATEX = invalide : le remplacement est rejeté, l'agrégat reste inchangé.
        var act = () => table.UpdateRule(
            "REGIME-A", MappingPart.Adjudication, FixedRule("REGIME-A", VatCategory.E, 0m, vatex: null));

        act.Should().Throw<InvalidMappingTableException>();
        table.Rules[0].Category.Should().Be(VatCategory.S, "le remplacement invalide n'a pas été appliqué.");
        table.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void RemoveRule_Removes_Returns_Removed_And_Invalidates()
    {
        var table = ValidatedTable(
            FixedRule("REGIME-A", VatCategory.S, 20m),
            FixedRule("REGIME-B", VatCategory.AA, 10m));

        var removed = table.RemoveRule("REGIME-A", MappingPart.Adjudication);

        removed.SourceRegimeCode.Should().Be("REGIME-A");
        table.Rules.Should().ContainSingle();
        table.Rules[0].SourceRegimeCode.Should().Be("REGIME-B");
        table.IsValidated.Should().BeFalse();
    }

    [Fact]
    public void RemoveRule_Not_Found_Throws_NotFound()
    {
        var table = ValidatedTable(FixedRule("REGIME-A", VatCategory.S, 20m));

        var act = () => table.RemoveRule("REGIME-A", MappingPart.Frais);

        act.Should().Throw<NotFoundException>();
        table.Rules.Should().ContainSingle();
        table.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void Validate_Sets_ValidatedBy_And_Today()
    {
        var table = MappingTable.Create(
            Guid.NewGuid(), "v1", null, null, MappingDefaultBehavior.Block,
            [FixedRule("REGIME-A", VatCategory.S, 20m)]);
        table.IsValidated.Should().BeFalse();

        table.Validate("Expert-comptable CMP");

        table.IsValidated.Should().BeTrue();
        table.ValidatedBy.Should().Be("Expert-comptable CMP");
        table.ValidatedDate.Should().Be(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
    }

    [Fact]
    public void Validate_Empty_ValidatedBy_Throws()
    {
        var table = MappingTable.Create(
            Guid.NewGuid(), "v1", null, null, MappingDefaultBehavior.Block,
            [FixedRule("REGIME-A", VatCategory.S, 20m)]);

        var act = () => table.Validate("   ");

        act.Should().Throw<ArgumentException>();
        table.IsValidated.Should().BeFalse();
    }

    [Fact]
    public void AddRule_Same_Code_Different_Part_Is_Allowed()
    {
        // Régime de la marge (F03 §2.3) : un même code → adjudication (E) + frais (S).
        var table = ValidatedTable(
            FixedRule("REGIME-MARGE", VatCategory.E, 0m, MappingPart.Adjudication, "VATEX-EU-J"));

        table.AddRule(FixedRule("REGIME-MARGE", VatCategory.S, 20m, MappingPart.Frais));

        table.Rules.Should().HaveCount(2);
    }
}
