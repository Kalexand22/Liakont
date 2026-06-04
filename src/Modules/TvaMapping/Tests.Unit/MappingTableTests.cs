namespace Liakont.Modules.TvaMapping.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Xunit;

/// <summary>
/// Validation structurelle de la table de mapping TVA (item TVA01 §3/§4/§5, F03 §2). Couvre les cas
/// que la table DOIT savoir refuser (E sans VATEX, doublons, taux incohérent, catégorie/part hors
/// liste) et l'état « NON VALIDÉE ».
/// </summary>
public sealed class MappingTableTests
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

    private static MappingTable Create(params MappingRule[] rules)
        => MappingTable.Create(Guid.NewGuid(), "v1", null, null, MappingDefaultBehavior.Block, rules);

    [Fact]
    public void Create_Valid_Table_Succeeds()
    {
        var table = Create(
            FixedRule("REGIME-A", VatCategory.S, 20m),
            FixedRule("REGIME-B", VatCategory.AA, 10m),
            FixedRule("REGIME-C", VatCategory.E, 0m, vatex: "VATEX-EU-J"));

        table.Rules.Should().HaveCount(3);
        table.MappingVersion.Should().Be("v1");
        table.DefaultBehavior.Should().Be(MappingDefaultBehavior.Block);
    }

    [Fact]
    public void Create_Empty_Table_Is_Valid()
    {
        // Une table sans règle est structurellement valide (elle bloque tout régime, defaultBehavior).
        var table = Create();
        table.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Create_E_AtZero_Without_Vatex_Throws()
    {
        // F03 §2.2 / item TVA01 §3 : une exonération à 0 % sans motif VATEX = table invalide.
        var act = () => Create(FixedRule("REGIME-X", VatCategory.E, 0m, vatex: null));

        act.Should().Throw<InvalidMappingTableException>()
            .Which.Violations.Should().ContainSingle(v => v.Contains("VATEX"));
    }

    [Fact]
    public void Create_E_AtZero_With_Vatex_Succeeds()
    {
        var table = Create(FixedRule("REGIME-X", VatCategory.E, 0m, vatex: "VATEX-EU-F"));
        table.Rules[0].Vatex.Should().Be("VATEX-EU-F");
    }

    [Fact]
    public void Create_Duplicate_Code_And_Part_Throws()
    {
        var act = () => Create(
            FixedRule("REGIME-A", VatCategory.S, 20m, MappingPart.Adjudication),
            FixedRule("REGIME-A", VatCategory.AA, 10m, MappingPart.Adjudication));

        act.Should().Throw<InvalidMappingTableException>()
            .Which.Violations.Should().Contain(v => v.Contains("doublon", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_Same_Code_Different_Part_Is_Allowed()
    {
        // Le régime de la marge (F03 §2.3) : un même code source → adjudication (E) + frais (S).
        var table = Create(
            FixedRule("REGIME-MARGE", VatCategory.E, 0m, MappingPart.Adjudication, "VATEX-EU-J"),
            FixedRule("REGIME-MARGE", VatCategory.S, 20m, MappingPart.Frais));

        table.Rules.Should().HaveCount(2);
    }

    [Fact]
    public void Create_Fixed_Rate_Without_Value_Throws()
    {
        var rule = new MappingRule
        {
            SourceRegimeCode = "REGIME-A",
            Part = MappingPart.Adjudication,
            Category = VatCategory.S,
            RateMode = RateMode.Fixed,
            RateValue = null,
        };

        var act = () => Create(rule);
        act.Should().Throw<InvalidMappingTableException>();
    }

    [Fact]
    public void Create_Computed_Rate_With_Fixed_Value_Throws()
    {
        var rule = new MappingRule
        {
            SourceRegimeCode = "REGIME-FRAIS",
            Part = MappingPart.Frais,
            Category = VatCategory.S,
            RateMode = RateMode.ComputedFromSource,
            RateValue = 20m,
        };

        var act = () => Create(rule);
        act.Should().Throw<InvalidMappingTableException>();
    }

    [Fact]
    public void Create_Computed_Rate_Without_Value_Succeeds()
    {
        // F03 §4.1 : le taux des frais peut être calculé depuis la source (rate null en table).
        var rule = new MappingRule
        {
            SourceRegimeCode = "REGIME-FRAIS",
            Part = MappingPart.Frais,
            Category = VatCategory.S,
            RateMode = RateMode.ComputedFromSource,
            RateValue = null,
        };

        var table = Create(rule);
        table.Rules[0].RateMode.Should().Be(RateMode.ComputedFromSource);
        table.Rules[0].RateValue.Should().BeNull();
    }

    [Fact]
    public void Create_Negative_Rate_Throws()
    {
        var act = () => Create(FixedRule("REGIME-A", VatCategory.S, -1m));
        act.Should().Throw<InvalidMappingTableException>();
    }

    [Fact]
    public void Create_Unknown_Category_Throws()
    {
        var rule = new MappingRule
        {
            SourceRegimeCode = "REGIME-A",
            Part = MappingPart.Adjudication,
            Category = (VatCategory)99,
            RateMode = RateMode.Fixed,
            RateValue = 20m,
        };

        var act = () => Create(rule);
        act.Should().Throw<InvalidMappingTableException>()
            .Which.Violations.Should().Contain(v => v.Contains("catégorie", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_Empty_Version_Throws()
    {
        var act = () => MappingTable.Create(
            Guid.NewGuid(), "  ", null, null, MappingDefaultBehavior.Block, [FixedRule("A", VatCategory.S, 20m)]);

        act.Should().Throw<InvalidMappingTableException>();
    }

    [Fact]
    public void Create_Aggregates_All_Violations()
    {
        var act = () => Create(
            FixedRule("REGIME-X", VatCategory.E, 0m, vatex: null),
            FixedRule("REGIME-Y", VatCategory.S, -5m));

        act.Should().Throw<InvalidMappingTableException>()
            .Which.Violations.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void IsValidated_Is_False_When_Validation_Absent()
    {
        var table = MappingTable.Create(
            Guid.NewGuid(), "v1", null, null, MappingDefaultBehavior.Block, [FixedRule("A", VatCategory.S, 20m)]);

        table.IsValidated.Should().BeFalse("une table sans validatedBy/validatedDate est « NON VALIDÉE » (item TVA01 §5).");
    }

    [Fact]
    public void IsValidated_Is_True_When_Both_Set()
    {
        var table = MappingTable.Create(
            Guid.NewGuid(),
            "v1",
            "Expert-comptable",
            new DateOnly(2026, 7, 15),
            MappingDefaultBehavior.Block,
            [FixedRule("A", VatCategory.S, 20m)]);

        table.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void IsValidated_Is_False_When_Only_ValidatedBy_Set()
    {
        var table = MappingTable.Create(
            Guid.NewGuid(), "v1", "Expert", null, MappingDefaultBehavior.Block, [FixedRule("A", VatCategory.S, 20m)]);

        table.IsValidated.Should().BeFalse();
    }

    [Fact]
    public void Reconstitute_Invalid_Table_Throws_At_Load()
    {
        // Chargement = re-validation (item TVA01 §4) : une table persistée corrompue lève au load.
        var act = () => MappingTable.Reconstitute(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            null,
            null,
            MappingDefaultBehavior.Block,
            [FixedRule("REGIME-X", VatCategory.E, 0m, vatex: null)],
            DateTimeOffset.UtcNow,
            null);

        act.Should().Throw<InvalidMappingTableException>();
    }
}
