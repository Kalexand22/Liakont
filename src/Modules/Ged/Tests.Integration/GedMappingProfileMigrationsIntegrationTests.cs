namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Mapping;
using Liakont.Modules.Ged.Infrastructure.Mapping;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Tests base-réelle du moteur de mapping GED (GED12, F19 §8) : persistance/rechargement d'un profil VALIDÉ
/// (round-trip des règles), NON-application d'un profil non validé, et journal <c>ged_mapping_change_log</c>
/// <b>append-only</b> (UPDATE/DELETE/TRUNCATE rejetés, CLAUDE.md n°4). Les goldens de mapping purs
/// (brut → axes + DEFER) sont couverts côté Domain (<c>GedMapperTests</c>).
/// </summary>
[Collection("GedIntegration")]
public sealed class GedMappingProfileMigrationsIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public GedMappingProfileMigrationsIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_validated_profile_round_trips_with_its_rules()
    {
        var factory = _fixture.CreateTenantDatabase();
        var repository = new GedMappingProfileRepository(factory);
        var profile = SampleProfile("typ_a", validated: true);

        await repository.InsertProfileAsync(
            profile,
            GedMappingChangeLogFactory.ForCreateProfile(profile, "ec@example.test", "Expert"));

        var loaded = await repository.GetValidatedProfileAsync("typ_a");

        loaded.Should().NotBeNull();
        loaded!.DocumentType.Should().Be("typ_a");
        loaded.IsValidated.Should().BeTrue();
        loaded.AxisRules.Should().HaveCount(2);
        loaded.AxisRules[0].Should().BeEquivalentTo(new AxisMappingRule("axe_date", "$.fields.date_champ", IsRequired: true, IsMulti: false));
        loaded.EntityRules.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new EntityMappingRule("ent_partenaire", "$.entities[?type=='partenaire'].externalId", null));
        loaded.RelationRules.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RelationMappingRule("concerne", "ent_parent", "$.fields.parent_champ"));
    }

    [Fact]
    public async Task An_unvalidated_profile_is_never_returned_as_applicable()
    {
        var factory = _fixture.CreateTenantDatabase();
        var repository = new GedMappingProfileRepository(factory);
        var profile = SampleProfile("typ_b", validated: false);

        await repository.InsertProfileAsync(
            profile,
            GedMappingChangeLogFactory.ForCreateProfile(profile, operatorIdentity: null, operatorName: null));

        (await repository.GetValidatedProfileAsync("typ_b")).Should().BeNull();
    }

    [Fact]
    public async Task Two_validated_profiles_for_the_same_document_type_conflict()
    {
        var factory = _fixture.CreateTenantDatabase();
        var repository = new GedMappingProfileRepository(factory);
        var first = SampleProfile("typ_c", validated: true);
        await repository.InsertProfileAsync(first, GedMappingChangeLogFactory.ForCreateProfile(first, null, null));

        var second = SampleProfile("typ_c", validated: true, version: "2");
        Func<Task> insertSecond = () => repository.InsertProfileAsync(
            second,
            GedMappingChangeLogFactory.ForCreateProfile(second, null, null));

        await insertSecond.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Change_log_rejects_update()
    {
        using var connection = await SeedChangeLogAsync();

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_catalog.ged_mapping_change_log SET change_type = 'tampered'");

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Change_log_rejects_delete()
    {
        using var connection = await SeedChangeLogAsync();

        Func<Task> delete = () => connection.ExecuteAsync("DELETE FROM ged_catalog.ged_mapping_change_log");

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Change_log_rejects_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Le trigger d'INSTRUCTION ferme le vecteur de purge en masse même sur une table vide.
        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_catalog.ged_mapping_change_log");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    private static GedMappingProfile SampleProfile(string documentType, bool validated, string version = "1")
    {
        return GedMappingProfile.Create(
            documentType,
            version,
            storagePolicy: "WormPlusIndex",
            validatedBy: validated ? "ec@example.test" : null,
            validatedDate: validated ? new DateOnly(2026, 1, 1) : null,
            axisRules: new[]
            {
                new AxisMappingRule("axe_date", "$.fields.date_champ", IsRequired: true, IsMulti: false),
                new AxisMappingRule("axe_ref", "$.axes[?name=='refs'].values[*]", IsRequired: false, IsMulti: true),
            },
            entityRules: new[]
            {
                new EntityMappingRule("ent_partenaire", "$.entities[?type=='partenaire'].externalId", null),
            },
            relationRules: new[]
            {
                new RelationMappingRule("concerne", "ent_parent", "$.fields.parent_champ"),
            },
            createdAt: DateTimeOffset.UnixEpoch);
    }

    private async Task<System.Data.IDbConnection> SeedChangeLogAsync()
    {
        var factory = _fixture.CreateTenantDatabase();
        var repository = new GedMappingProfileRepository(factory);
        var profile = SampleProfile("typ_a", validated: true);

        // InsertProfileAsync écrit le profil ET une entrée de change-log dans la même transaction.
        await repository.InsertProfileAsync(
            profile,
            GedMappingChangeLogFactory.ForCreateProfile(profile, "ec@example.test", "Expert"));

        return await factory.OpenAsync();
    }
}
