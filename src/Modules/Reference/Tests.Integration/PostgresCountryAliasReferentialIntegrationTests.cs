namespace Liakont.Modules.Reference.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.Reference.Infrastructure;
using Xunit;

/// <summary>
/// Référentiel de correspondance pays sur PostgreSQL réel (ADR-0038) : round-trip en base SYSTÈME (sans
/// <c>tenant_id</c>, via <c>ISystemConnectionFactory</c>), normalisation de la clé (casse/espaces), cible
/// non-ISO refusée à l'écriture, invalidation de cache après mutation, et journal APPEND-ONLY (une entrée par
/// mutation ; UPDATE/DELETE rejetés par trigger). Chaque test utilise des codes source distincts pour ne pas
/// interférer (fixture de collection partagée = une seule base).
/// </summary>
[Collection("ReferenceIntegration")]
public sealed class PostgresCountryAliasReferentialIntegrationTests
{
    private readonly ReferenceDatabaseFixture _fixture;

    public PostgresCountryAliasReferentialIntegrationTests(ReferenceDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seed_maps_the_universal_legacy_codes()
    {
        var store = NewStore();

        (await store.ResolveAsync("ENG")).Should().Be("GB");
        (await store.ResolveAsync("JAP")).Should().Be("JP");
    }

    [Fact]
    public async Task Upsert_then_resolve_round_trips_normalizing_the_key_case_and_spaces()
    {
        var store = NewStore();

        await store.UpsertAsync(" bel ", "be", Guid.NewGuid(), "Alice");

        (await store.ResolveAsync("BEL")).Should().Be("BE");
        (await store.ResolveAsync("bel")).Should().Be("BE");
        (await store.ResolveAsync(" bel ")).Should().Be("BE");

        (await store.GetAliasesAsync()).Should().Contain(a => a.SourceCode == "BEL" && a.IsoCode == "BE");
    }

    [Fact]
    public async Task Upsert_rejects_a_non_iso_target_at_write_time()
    {
        var store = NewStore();

        Func<Task> act = async () => await store.UpsertAsync("ZZ", "QQ", Guid.NewGuid(), "Alice");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISO 3166-1*");

        // Validation AVANT écriture : rien n'a été stocké, le code source reste non résolu.
        (await store.ResolveAsync("ZZ")).Should().Be("ZZ");
    }

    [Fact]
    public async Task Resolve_reflects_a_new_alias_after_the_cache_is_invalidated_by_a_write()
    {
        var store = NewStore();

        // Amorce le cache (le code n'existe pas encore → renvoyé brut).
        (await store.ResolveAsync("PRT")).Should().Be("PRT");

        await store.UpsertAsync("PRT", "PT", Guid.NewGuid(), "Alice");

        // L'écriture a invalidé le cache → la nouvelle correspondance est vue au run suivant.
        (await store.ResolveAsync("PRT")).Should().Be("PT");
    }

    [Fact]
    public async Task Remove_deletes_the_alias_and_is_a_noop_on_an_unknown_source()
    {
        var store = NewStore();

        await store.UpsertAsync("SCT", "GB", Guid.NewGuid(), "Alice");
        (await store.ResolveAsync("SCT")).Should().Be("GB");

        (await store.RemoveAsync("SCT", Guid.NewGuid(), "Alice")).Should().BeTrue();
        (await store.ResolveAsync("SCT")).Should().Be("SCT");

        (await store.RemoveAsync("SCT", Guid.NewGuid(), "Alice")).Should().BeFalse("rien à supprimer → aucun effet");
    }

    [Fact]
    public async Task Every_mutation_writes_an_append_only_journal_entry()
    {
        var store = NewStore();
        await store.UpsertAsync("AUT", "AT", Guid.NewGuid(), "Alice");

        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM reference.country_alias_change_log WHERE source_code = 'AUT'");

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_change_log_rejects_update_delete_and_truncate()
    {
        var store = NewStore();
        await store.UpsertAsync("NLD", "NL", Guid.NewGuid(), "Alice");

        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();

        Func<Task> update = async () => await connection.ExecuteAsync(
            "UPDATE reference.country_alias_change_log SET operator_name = 'x' WHERE source_code = 'NLD'");
        await update.Should().ThrowAsync<Npgsql.PostgresException>();

        Func<Task> delete = async () => await connection.ExecuteAsync(
            "DELETE FROM reference.country_alias_change_log WHERE source_code = 'NLD'");
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();

        // Purge en masse du journal d'audit (CLAUDE.md n°4) : le trigger d'INSTRUCTION anti-TRUNCATE (V002) doit
        // la rejeter — sans cette assertion, une régression sur ce trigger passerait au vert (faux-vert).
        Func<Task> truncate = async () => await connection.ExecuteAsync(
            "TRUNCATE reference.country_alias_change_log");
        await truncate.Should().ThrowAsync<Npgsql.PostgresException>();

        // Le journal est intact : aucune des trois tentatives n'a muté la ligne.
        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM reference.country_alias_change_log WHERE source_code = 'NLD'");
        count.Should().BeGreaterThan(0);
    }

    private PostgresCountryAliasReferential NewStore() => new(_fixture.CreateConnectionFactory());
}
