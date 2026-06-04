namespace Liakont.Modules.TvaMapping.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Domain;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;
using Liakont.Modules.TvaMapping.Tests.Integration.Doubles;
using Liakont.Modules.TvaMapping.Tests.Integration.Fixtures;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Édition de la table de mapping TVA (item TVA05) sur PostgreSQL réel (Testcontainers) : ajout /
/// modification / suppression / validation via les handlers MediatR, avec invalidation de la validation
/// à chaque mutation, journal append-only (trigger base) atomique avec la mutation, et isolation tenant.
/// </summary>
[Collection("TvaMappingIntegration")]
public sealed class MappingEditingIntegrationTests
{
    private readonly TvaMappingDatabaseFixture _fixture;

    public MappingEditingIntegrationTests(TvaMappingDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddRule_Persists_Rule_Invalidates_Table_And_Logs()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyId, validatedBy: "Expert-comptable", FixedRule("REGIME-A", VatCategory.S, 20m));

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new AddMappingRuleHandler(harness.UowFactory, filter, accessor);
        await handler.Handle(
            new AddMappingRuleCommand
            {
                SourceRegimeCode = "REGIME-B",
                Part = "Adjudication",
                Category = "AA",
                RateMode = "Fixed",
                RateValue = 10m,
            },
            CancellationToken.None);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules.Should().HaveCount(2);
        dto.IsValidated.Should().BeFalse("toute mutation repasse la table « NON VALIDÉE » (item TVA05 §2).");

        var log = await ReadChangeLogAsync(harness, companyId);
        log.Should().ContainSingle();
        log[0].ChangeType.Should().Be((int)MappingChangeType.AddRule);
        log[0].SourceRegimeCode.Should().Be("REGIME-B");
        log[0].OperatorId.Should().Be(operatorId);
        log[0].OperatorName.Should().Be("Comptable de test");
        log[0].BeforeValue.Should().BeNull();
        log[0].AfterValue.Should().NotBeNull();
        log[0].AfterValue.Should().Contain("AA");
    }

    [Fact]
    public async Task AddRule_Invalid_Is_Rejected_And_Nothing_Persisted()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyId, validatedBy: "Expert-comptable", FixedRule("REGIME-A", VatCategory.S, 20m));

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new AddMappingRuleHandler(harness.UowFactory, filter, accessor);

        // E à 0 % sans VATEX = règle invalide (F03 §2.2).
        var act = () => handler.Handle(
            new AddMappingRuleCommand
            {
                SourceRegimeCode = "REGIME-X",
                Part = "Adjudication",
                Category = "E",
                Vatex = null,
                RateMode = "Fixed",
                RateValue = 0m,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMappingTableException>();

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules.Should().ContainSingle("la règle invalide n'a pas été persistée.");
        dto.IsValidated.Should().BeTrue("la validation n'est pas effacée si la mutation échoue.");
        (await ChangeLogCountAsync(harness, companyId)).Should().Be(0, "aucune entrée de journal pour une mutation rejetée.");
    }

    [Fact]
    public async Task UpdateRule_Invalidates_And_Logs_Before_After()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyId, validatedBy: "Expert-comptable", FixedRule("REGIME-A", VatCategory.S, 20m));

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new UpdateMappingRuleHandler(harness.UowFactory, filter, accessor);
        await handler.Handle(
            new UpdateMappingRuleCommand
            {
                SourceRegimeCode = "REGIME-A",
                Part = "Adjudication",
                Category = "AA",
                RateMode = "Fixed",
                RateValue = 10m,
            },
            CancellationToken.None);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules[0].Category.Should().Be("AA");
        dto.Rules[0].RateValue.Should().Be(10m);
        dto.IsValidated.Should().BeFalse();

        var log = await ReadChangeLogAsync(harness, companyId);
        log.Should().ContainSingle();
        log[0].ChangeType.Should().Be((int)MappingChangeType.UpdateRule);
        log[0].BeforeValue.Should().Contain("\"S\"");
        log[0].AfterValue.Should().Contain("\"AA\"");
    }

    [Fact]
    public async Task RemoveRule_Removes_Invalidates_And_Logs()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(
            harness, companyId, validatedBy: "Expert-comptable",
            FixedRule("REGIME-A", VatCategory.S, 20m),
            FixedRule("REGIME-B", VatCategory.AA, 10m));

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new RemoveMappingRuleHandler(harness.UowFactory, filter, accessor);
        await handler.Handle(
            new RemoveMappingRuleCommand { SourceRegimeCode = "REGIME-A", Part = "Adjudication" },
            CancellationToken.None);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules.Should().ContainSingle();
        dto.Rules[0].SourceRegimeCode.Should().Be("REGIME-B");
        dto.IsValidated.Should().BeFalse();

        var log = await ReadChangeLogAsync(harness, companyId);
        log.Should().ContainSingle();
        log[0].ChangeType.Should().Be((int)MappingChangeType.RemoveRule);
        log[0].SourceRegimeCode.Should().Be("REGIME-A");
        log[0].BeforeValue.Should().NotBeNull();
        log[0].AfterValue.Should().BeNull();
    }

    [Fact]
    public async Task Validate_Marks_Table_Validated_And_Logs()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyId, validatedBy: null, FixedRule("REGIME-A", VatCategory.S, 20m));

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new ValidateMappingTableHandler(harness.UowFactory, filter, accessor);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        await handler.Handle(new ValidateMappingTableCommand { ValidatedBy = "Expert-comptable CMP" }, CancellationToken.None);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.IsValidated.Should().BeTrue();
        dto.ValidatedBy.Should().Be("Expert-comptable CMP");

        // today ou today+1 si l'exécution franchit minuit UTC entre la capture et l'appel (anti-flake).
        dto.ValidatedDate.Should().BeOneOf(today, today.AddDays(1));

        var log = await ReadChangeLogAsync(harness, companyId);
        log.Should().ContainSingle();
        log[0].ChangeType.Should().Be((int)MappingChangeType.Validate);
        log[0].SourceRegimeCode.Should().BeNull("une validation ne porte pas sur une règle particulière.");
        log[0].AfterValue.Should().Contain("Expert-comptable CMP");
    }

    [Fact]
    public async Task Mutation_On_Absent_Table_Throws_NotFound()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new AddMappingRuleHandler(harness.UowFactory, filter, accessor);
        var act = () => handler.Handle(
            new AddMappingRuleCommand
            {
                SourceRegimeCode = "REGIME-A",
                Part = "Adjudication",
                Category = "S",
                RateMode = "Fixed",
                RateValue = 20m,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        (await ChangeLogCountAsync(harness, companyId)).Should().Be(0);
    }

    [Fact]
    public async Task Tenant_Isolation_Mutation_Does_Not_Touch_Other_Tenant()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyA, validatedBy: "Expert A", FixedRule("REGIME-A", VatCategory.S, 20m));
        await SeedTableAsync(harness, companyB, validatedBy: "Expert B", FixedRule("REGIME-A", VatCategory.S, 20m));

        var (filter, accessor) = Deps(operatorId, companyA);
        var handler = new AddMappingRuleHandler(harness.UowFactory, filter, accessor);
        await handler.Handle(
            new AddMappingRuleCommand
            {
                SourceRegimeCode = "REGIME-B",
                Part = "Adjudication",
                Category = "AA",
                RateMode = "Fixed",
                RateValue = 10m,
            },
            CancellationToken.None);

        var dtoA = await harness.Queries.GetMappingTable(companyA);
        var dtoB = await harness.Queries.GetMappingTable(companyB);

        dtoA!.Rules.Should().HaveCount(2);
        dtoA.IsValidated.Should().BeFalse();
        dtoB!.Rules.Should().ContainSingle("la mutation sur A ne touche pas B (isolation tenant, CLAUDE.md n°9).");
        dtoB.IsValidated.Should().BeTrue("le tenant B reste validé.");

        (await ChangeLogCountAsync(harness, companyA)).Should().Be(1);
        (await ChangeLogCountAsync(harness, companyB)).Should().Be(0);
    }

    [Fact]
    public async Task ChangeLog_Is_Append_Only_Update_And_Delete_Rejected()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyId, validatedBy: "Expert", FixedRule("REGIME-A", VatCategory.S, 20m));

        var (filter, accessor) = Deps(operatorId, companyId);
        var handler = new AddMappingRuleHandler(harness.UowFactory, filter, accessor);
        await handler.Handle(
            new AddMappingRuleCommand
            {
                SourceRegimeCode = "REGIME-B",
                Part = "Adjudication",
                Category = "AA",
                RateMode = "Fixed",
                RateValue = 10m,
            },
            CancellationToken.None);

        using var conn = await harness.ConnectionFactory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE tvamapping.mapping_change_log SET operator_name = 'falsifié' WHERE company_id = @c",
            new { c = companyId });
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM tvamapping.mapping_change_log WHERE company_id = @c",
            new { c = companyId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
        await delete.Should().ThrowAsync<PostgresException>();

        (await ChangeLogCountAsync(harness, companyId)).Should().Be(1, "ni l'UPDATE ni le DELETE n'ont abouti.");
    }

    [Fact]
    public async Task Mutation_And_ChangeLog_Are_Atomic_Abandoned_Transaction_Persists_Nothing()
    {
        // Atomicité (item TVA05 §5) : mutation + entrée de journal dans la MÊME transaction. Une
        // transaction abandonnée (échec avant le commit) ne laisse RIEN — ni règle, ni journal.
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await SeedTableAsync(harness, companyId, validatedBy: "Expert", FixedRule("REGIME-A", VatCategory.S, 20m));

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var table = await uow.GetForUpdateAsync(companyId);
            var added = FixedRule("REGIME-B", VatCategory.AA, 10m);
            table!.AddRule(added);
            var entry = MappingChangeLogFactory.ForAddRule(table, added, operatorId, "Comptable de test");
            await uow.SaveMutationAsync(table, entry);

            // Pas de CommitAsync : la sortie du bloc déclenche le rollback (TransactionScope.DisposeAsync).
        }

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto!.Rules.Should().ContainSingle("la mutation non validée a été annulée.");
        dto.IsValidated.Should().BeTrue();
        (await ChangeLogCountAsync(harness, companyId)).Should().Be(0, "l'entrée de journal a été annulée avec la mutation.");
    }

    private static MappingRule FixedRule(
        string code, VatCategory category, decimal rate,
        MappingPart part = MappingPart.Adjudication, string? vatex = null) => new()
        {
            SourceRegimeCode = code,
            Part = part,
            Category = category,
            Vatex = vatex,
            RateMode = RateMode.Fixed,
            RateValue = rate,
        };

    private static (TestCompanyFilter Filter, TestActorContextAccessor Accessor) Deps(Guid operatorId, Guid companyId)
    {
        var accessor = new TestActorContextAccessor(operatorId, companyId);
        return (new TestCompanyFilter(accessor), accessor);
    }

    private static async Task SeedTableAsync(
        TvaMappingHarness harness, Guid companyId, string? validatedBy, params MappingRule[] rules)
    {
        var table = MappingTable.Create(
            companyId,
            "cmp-v1",
            validatedBy,
            validatedBy is null ? null : new DateOnly(2026, 7, 15),
            MappingDefaultBehavior.Block,
            rules);

        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
    }

    private static async Task<int> ChangeLogCountAsync(TvaMappingHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM tvamapping.mapping_change_log WHERE company_id = @c",
            new { c = companyId });
    }

    private static async Task<IReadOnlyList<ChangeLogRow>> ReadChangeLogAsync(TvaMappingHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        var rows = await conn.QueryAsync(
            """
            SELECT change_type, source_regime_code, part, before_value, after_value,
                   operator_id, operator_name, mapping_version
            FROM tvamapping.mapping_change_log
            WHERE company_id = @c
            ORDER BY occurred_at
            """,
            new { c = companyId });

        var result = new List<ChangeLogRow>();
        foreach (var r in rows)
        {
            result.Add(new ChangeLogRow(
                (int)r.change_type,
                (string?)r.source_regime_code,
                (int?)r.part,
                (string?)r.before_value,
                (string?)r.after_value,
                (Guid)r.operator_id,
                (string?)r.operator_name,
                (string)r.mapping_version));
        }

        return result;
    }

    private sealed record ChangeLogRow(
        int ChangeType,
        string? SourceRegimeCode,
        int? Part,
        string? BeforeValue,
        string? AfterValue,
        Guid OperatorId,
        string? OperatorName,
        string MappingVersion);
}
