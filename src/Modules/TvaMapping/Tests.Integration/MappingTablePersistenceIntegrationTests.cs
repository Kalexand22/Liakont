namespace Liakont.Modules.TvaMapping.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Persistance de la table de mapping TVA (item TVA01) sur PostgreSQL réel (Testcontainers) :
/// round-trip, flags source, taux calculé, état « NON VALIDÉE », isolation par tenant, conflit
/// d'unicité, et re-validation au chargement d'une table corrompue.
/// </summary>
[Collection("TvaMappingIntegration")]
public sealed class MappingTablePersistenceIntegrationTests
{
    private readonly TvaMappingDatabaseFixture _fixture;

    public MappingTablePersistenceIntegrationTests(TvaMappingDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_And_Get_RoundTrips_All_Fields()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        var table = MappingTable.Create(
            companyId,
            "cmp-v1",
            "Expert-comptable",
            new DateOnly(2026, 7, 15),
            MappingDefaultBehavior.Block,
            new MappingRule[]
            {
                new()
                {
                    SourceRegimeCode = "REGIME-A",
                    Label = "Assujetti 20 %",
                    Part = MappingPart.Adjudication,
                    Category = VatCategory.S,
                    RateMode = RateMode.Fixed,
                    RateValue = 20m,
                },
                new()
                {
                    SourceRegimeCode = "REGIME-B",
                    Label = "Super réduit",
                    Part = MappingPart.Adjudication,
                    Category = VatCategory.AAA,
                    RateMode = RateMode.Fixed,
                    RateValue = 2.1m,
                },
            });

        await InsertAsync(harness, table);

        var dto = await harness.Queries.GetMappingTable(companyId);

        dto.Should().NotBeNull();
        dto!.MappingVersion.Should().Be("cmp-v1");
        dto.ValidatedBy.Should().Be("Expert-comptable");
        dto.ValidatedDate.Should().Be(new DateOnly(2026, 7, 15));
        dto.IsValidated.Should().BeTrue();
        dto.DefaultBehavior.Should().Be("Block");
        dto.Rules.Should().HaveCount(2);
        dto.Rules[0].SourceRegimeCode.Should().Be("REGIME-A");
        dto.Rules[0].Category.Should().Be("S");
        dto.Rules[0].RateValue.Should().Be(20m);
        dto.Rules[1].Category.Should().Be("AAA");
        dto.Rules[1].RateValue.Should().Be(2.1m);
    }

    [Fact]
    public async Task Get_Returns_Null_When_No_Table_For_Tenant()
    {
        var harness = new TvaMappingHarness(_fixture);
        var dto = await harness.Queries.GetMappingTable(Guid.NewGuid());
        dto.Should().BeNull();
    }

    [Fact]
    public async Task Rule_With_SourceFlags_RoundTrips()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        var table = MappingTable.Create(
            companyId,
            "v1",
            null,
            null,
            MappingDefaultBehavior.Block,
            new MappingRule[]
            {
                new()
                {
                    SourceRegimeCode = "REGIME-MARGE",
                    Part = MappingPart.Adjudication,
                    SourceFlags = new Dictionary<string, string> { ["RegimeMarge"] = "true", ["assujetti_tva"] = "false" },
                    Category = VatCategory.E,
                    Vatex = "VATEX-EU-J",
                    RateMode = RateMode.Fixed,
                    RateValue = 0m,
                },
            });

        await InsertAsync(harness, table);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules[0].SourceFlags.Should().NotBeNull();
        dto.Rules[0].SourceFlags!.Should().ContainKey("RegimeMarge").WhoseValue.Should().Be("true");
        dto.Rules[0].SourceFlags!["assujetti_tva"].Should().Be("false");
        dto.Rules[0].Vatex.Should().Be("VATEX-EU-J");
    }

    [Fact]
    public async Task Computed_Rate_Rule_RoundTrips_With_Null_Rate()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        var table = MappingTable.Create(
            companyId,
            "v1",
            null,
            null,
            MappingDefaultBehavior.Block,
            new MappingRule[]
            {
                new()
                {
                    SourceRegimeCode = "REGIME-FRAIS",
                    Part = MappingPart.Frais,
                    Category = VatCategory.S,
                    RateMode = RateMode.ComputedFromSource,
                    RateValue = null,
                },
            });

        await InsertAsync(harness, table);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules[0].RateMode.Should().Be("ComputedFromSource");
        dto.Rules[0].RateValue.Should().BeNull();
    }

    [Fact]
    public async Task NonValidated_Table_Is_Loadable_And_Flagged_NonValidee()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        var table = MappingTable.Create(
            companyId,
            "v1",
            null,
            null,
            MappingDefaultBehavior.Block,
            new MappingRule[]
            {
                new()
                {
                    SourceRegimeCode = "REGIME-A",
                    Part = MappingPart.Adjudication,
                    Category = VatCategory.S,
                    RateMode = RateMode.Fixed,
                    RateValue = 20m,
                },
            });

        await InsertAsync(harness, table);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto.Should().NotBeNull();
        dto!.IsValidated.Should().BeFalse("une table sans validatedBy/validatedDate est « NON VALIDÉE » mais reste chargeable (item TVA01 §5).");
        dto.ValidatedBy.Should().BeNull();
        dto.ValidatedDate.Should().BeNull();
    }

    [Fact]
    public async Task Tenant_Isolation_Is_Enforced()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await InsertAsync(harness, SingleRuleTable(companyA, "vA", VatCategory.S, 20m));
        await InsertAsync(harness, SingleRuleTable(companyB, "vB", VatCategory.AA, 10m));

        var dtoA = await harness.Queries.GetMappingTable(companyA);
        var dtoB = await harness.Queries.GetMappingTable(companyB);

        dtoA!.MappingVersion.Should().Be("vA");
        dtoA.Rules[0].RateValue.Should().Be(20m);
        dtoB!.MappingVersion.Should().Be("vB");
        dtoB.Rules[0].RateValue.Should().Be(10m);
    }

    [Fact]
    public async Task Insert_Duplicate_Table_For_Same_Tenant_Throws_Conflict()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        await InsertAsync(harness, SingleRuleTable(companyId, "v1", VatCategory.S, 20m));

        var act = () => InsertAsync(harness, SingleRuleTable(companyId, "v2", VatCategory.S, 20m));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Load_Of_Structurally_Invalid_Table_Throws_At_Load()
    {
        // Table corrompue par une édition directe (E à 0 % sans VATEX) : le chargement re-valide et
        // lève (item TVA01 §4) plutôt que de servir une exonération sans motif.
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        await RawInsertSingleRuleAsync(harness, companyId, category: (int)VatCategory.E, rateValue: 0m, vatex: null);

        var act = () => harness.Queries.GetMappingTable(companyId);
        await act.Should().ThrowAsync<InvalidMappingTableException>();
    }

    [Fact]
    public async Task Load_Of_Unknown_Category_Throws_At_Load()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        await RawInsertSingleRuleAsync(harness, companyId, category: 99, rateValue: 20m, vatex: null);

        var act = () => harness.Queries.GetMappingTable(companyId);
        await act.Should().ThrowAsync<InvalidMappingTableException>();
    }

    private static MappingTable SingleRuleTable(Guid companyId, string version, VatCategory category, decimal rate)
        => MappingTable.Create(
            companyId,
            version,
            null,
            null,
            MappingDefaultBehavior.Block,
            new MappingRule[]
            {
                new()
                {
                    SourceRegimeCode = "REGIME-A",
                    Part = MappingPart.Adjudication,
                    Category = category,
                    RateMode = RateMode.Fixed,
                    RateValue = rate,
                },
            });

    private static async Task InsertAsync(TvaMappingHarness harness, MappingTable table)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
    }

    private static async Task RawInsertSingleRuleAsync(
        TvaMappingHarness harness,
        Guid companyId,
        int category,
        decimal? rateValue,
        string? vatex)
    {
        // Insertion SQL directe, en contournant le validateur de domaine, pour simuler une donnée
        // persistée corrompue (édition manuelle / régression) et prouver la re-validation au load.
        using var conn = await harness.ConnectionFactory.OpenAsync();
        var tableId = Guid.NewGuid();

        await conn.ExecuteAsync(
            """
            INSERT INTO tvamapping.mapping_tables (id, company_id, mapping_version, default_behavior, created_at)
            VALUES (@Id, @CompanyId, 'corrompue', 0, now())
            """,
            new { Id = tableId, CompanyId = companyId });

        await conn.ExecuteAsync(
            """
            INSERT INTO tvamapping.mapping_rules
                (table_id, ordinal, source_regime_code, part, category, vatex, rate_mode, rate_value)
            VALUES (@TableId, 0, 'REGIME-X', 0, @Category, @Vatex, 0, @RateValue)
            """,
            new { TableId = tableId, Category = category, Vatex = vatex, RateValue = rateValue });
    }
}
