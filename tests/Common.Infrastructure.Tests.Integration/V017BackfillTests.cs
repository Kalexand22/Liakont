namespace Stratum.Common.Infrastructure.Tests.Integration;

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using DbUp.Engine;
using FluentAssertions;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Prouve le BACKFILL et la garde fail-closed de la migration V017 (ADR-0021 §2c / RLM02) sur le chemin
/// de MISE À NIVEAU. <see cref="DatabaseFixture"/> migre tout d'un coup (et ne seede aucun <c>default</c>),
/// donc ces cas exigent une migration en DEUX PASSES sur une base fraîche. xUnit instancie la classe (et
/// donc le conteneur) une fois par méthode de test : chaque test part d'une base vierge.
/// </summary>
public sealed class V017BackfillTests : IAsyncLifetime
{
    private const string DefaultCompanyId = "00000000-0000-4000-a000-000000000001";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task V017_Backfills_Legacy_Default_Tenant_With_Null_CompanyId()
    {
        var connectionString = _container.GetConnectionString();
        EnsureOutboxSchema(connectionString);

        // Passe 1 : migrer jusqu'à V016 inclus → outbox.tenants existe, company_id nullable.
        RunMigrations(connectionString, version => version <= 16).Successful.Should().BeTrue();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            // Tenant `default` hérité, AVEC company_id NULL (état exact que V017 doit rattraper).
            await connection.ExecuteAsync(
                """
                INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name)
                VALUES ('default', 'Tenant par defaut', 'dev@liakont.local', 'liakont', 'liakont-dev')
                """);

            var before = await connection.ExecuteScalarAsync<Guid?>(
                "SELECT company_id FROM outbox.tenants WHERE id = 'default'");
            before.Should().BeNull("pré-condition : le default est NULL avant V017");
        }

        // Passe 2 : appliquer V017 → backfill + NOT NULL + UNIQUE.
        RunMigrations(connectionString, version => version == 17).Successful.Should().BeTrue();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var after = await connection.ExecuteScalarAsync<Guid?>(
                "SELECT company_id FROM outbox.tenants WHERE id = 'default'");

            after.Should().Be(
                Guid.Parse(DefaultCompanyId),
                "V017 backfille le tenant default vers le company_id canonique de dev");
        }
    }

    [Fact]
    public async Task V017_Fails_With_Explicit_Message_When_A_Non_Default_Tenant_Has_Null_CompanyId()
    {
        var connectionString = _container.GetConnectionString();
        EnsureOutboxSchema(connectionString);
        RunMigrations(connectionString, version => version <= 16).Successful.Should().BeTrue();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            // Tenant orphelin (NULL) AUTRE que `default` : V017 ne doit PAS inventer sa société — il
            // échoue avec un message opérateur explicite qui le nomme (CLAUDE.md n°12), pas une
            // not_null_violation brute.
            await connection.ExecuteAsync(
                """
                INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name)
                VALUES ('legacy-orphan', 'Legacy', 'a@b.test', 'db_legacy', 'realm_legacy')
                """);
        }

        var result = RunMigrations(connectionString, version => version == 17);

        result.Successful.Should().BeFalse("un tenant sans company_id (hors default) bloque la mise à niveau");
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Contain(
            "legacy-orphan", "le message nomme le tenant fautif pour guider l'opérateur");
    }

    private static DatabaseUpgradeResult RunMigrations(string connectionString, Func<int, bool> versionFilter)
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                assembly,
                script => script.Contains(".Migrations.", StringComparison.Ordinal)
                          && MatchesVersion(script, versionFilter))
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        return upgrader.PerformUpgrade();
    }

    private static bool MatchesVersion(string scriptName, Func<int, bool> versionFilter)
    {
        var match = Regex.Match(scriptName, @"\.V(\d+)__");
        return match.Success && versionFilter(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private static void EnsureOutboxSchema(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE SCHEMA IF NOT EXISTS outbox;";
        command.ExecuteNonQuery();
    }
}
