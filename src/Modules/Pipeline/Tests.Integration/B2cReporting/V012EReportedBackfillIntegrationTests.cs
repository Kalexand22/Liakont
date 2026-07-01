namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using DbUp.Engine;
using FluentAssertions;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Prouve le BACKFILL one-shot de la migration <c>V012</c> (BUG-24 / ADR-0037 §7) sur le chemin de MISE À
/// NIVEAU : un document figé <c>ReadyToSend</c> mais déjà e-reporté (entrée <c>pipeline.b2c_margin_emissions</c>
/// <c>status = 'Issued'</c>) est rétro-corrigé vers <c>EReported</c>, tandis que les documents qui NE remplissent
/// PAS strictement les deux conditions restent intacts (clause <c>WHERE</c> discriminante, mutation d'un champ
/// d'audit fiscal). L'isolation est STRUCTURELLE (database-per-tenant, blueprint §7 : aucune colonne
/// <c>tenant_id</c> — un backfill n'atteint que la base de SON tenant). Les fixtures partagées migrent tout d'un
/// coup ; ces cas exigent une migration en DEUX PASSES sur une base fraîche (seed de l'état « coincé » entre les
/// deux passes). xUnit instancie la classe (et donc le conteneur) une fois par méthode → base vierge par test.
/// </summary>
public sealed class V012EReportedBackfillIntegrationTests : IAsyncLifetime
{
    private static readonly Assembly DocumentsAssembly = typeof(DocumentsModuleRegistration).Assembly;
    private static readonly Assembly PipelineAssembly = typeof(PipelineModuleRegistration).Assembly;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task V012_Backfills_Only_ReadyToSend_Documents_Carrying_An_Issued_B2c_Emission()
    {
        var connectionString = _container.GetConnectionString();

        // Passe 1 : socle + documents JUSQU'À V011 (avant le backfill) + pipeline (crée b2c_margin_emissions).
        RunCommonMigrations(connectionString);
        RunAssemblyMigrations(connectionString, DocumentsAssembly, version => version <= 11).Successful.Should().BeTrue();
        RunAssemblyMigrations(connectionString, PipelineAssembly, _ => true).Successful.Should().BeTrue();

        // Cible du backfill : ReadyToSend + émission Issued (l'état « coincé » que BUG-24 corrige).
        var stuckId = Guid.NewGuid();

        // Contrôles : aucun ne doit être muté par V012.
        var noEmissionId = Guid.NewGuid();   // ReadyToSend SANS émission → différé, pas reporté.
        var pendingOnlyId = Guid.NewGuid();   // ReadyToSend + émission Pending (POST non confirmé) → pas reporté.
        var alreadyIssuedId = Guid.NewGuid(); // état 'Issued' (voie document) + émission Issued → hors périmètre WHERE.

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            await SeedDocumentAsync(connection, stuckId, "ReadyToSend");
            await SeedEmissionAsync(connection, stuckId, "Issued");

            await SeedDocumentAsync(connection, noEmissionId, "ReadyToSend");

            await SeedDocumentAsync(connection, pendingOnlyId, "ReadyToSend");
            await SeedEmissionAsync(connection, pendingOnlyId, "Pending");

            await SeedDocumentAsync(connection, alreadyIssuedId, "Issued");
            await SeedEmissionAsync(connection, alreadyIssuedId, "Issued");
        }

        // Passe 2 : appliquer le backfill V012.
        RunAssemblyMigrations(connectionString, DocumentsAssembly, version => version == 12).Successful.Should().BeTrue();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            (await StateOfAsync(connection, stuckId)).Should()
                .Be("EReported", "un document ReadyToSend porteur d'une émission B2C Issued est rétro-corrigé vers EReported");
            (await StateOfAsync(connection, noEmissionId)).Should()
                .Be("ReadyToSend", "un document sans émission n'est pas reporté — il reste ReadyToSend");
            (await StateOfAsync(connection, pendingOnlyId)).Should()
                .Be("ReadyToSend", "une émission seulement Pending (POST non confirmé) n'est pas un report accepté");
            (await StateOfAsync(connection, alreadyIssuedId)).Should()
                .Be("Issued", "le backfill ne touche QUE les documents ReadyToSend (clause WHERE) — un Issued reste Issued");
        }
    }

    [Fact]
    public async Task V012_Is_A_Safe_NoOp_When_The_Pipeline_Schema_Is_Absent()
    {
        var connectionString = _container.GetConnectionString();

        // Passe 1 : socle + documents JUSQU'À V011, SANS jamais migrer le schéma pipeline (b2c_margin_emissions absente).
        RunCommonMigrations(connectionString);
        RunAssemblyMigrations(connectionString, DocumentsAssembly, version => version <= 11).Successful.Should().BeTrue();

        var documentId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SeedDocumentAsync(connection, documentId, "ReadyToSend");
        }

        // Passe 2 : la garde to_regclass rend V012 SÛR quel que soit l'ordre inter-modules — schéma pipeline absent
        // ⇒ aucun document reporté ne peut exister ⇒ no-op (la migration RÉUSSIT, l'état reste inchangé).
        var result = RunAssemblyMigrations(connectionString, DocumentsAssembly, version => version == 12);
        result.Successful.Should().BeTrue("la garde to_regclass évite toute erreur quand pipeline.b2c_margin_emissions n'existe pas encore");

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            (await StateOfAsync(connection, documentId)).Should()
                .Be("ReadyToSend", "sans schéma pipeline, V012 ne touche aucun état (no-op sûr)");
        }
    }

    private static Task<int> SeedDocumentAsync(NpgsqlConnection connection, Guid id, string state) =>
        connection.ExecuteAsync(
            """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 total_net, total_tax, total_gross, state, payload_hash)
            VALUES
                (@id, @sourceReference, @documentNumber, 'FAC', DATE '2026-01-20',
                 100.00, 20.00, 120.00, @state, @payloadHash)
            """,
            new
            {
                id,
                sourceReference = "ba-" + id.ToString("N"),
                documentNumber = "F-" + id.ToString("N"),
                state,
                payloadHash = "hash-" + id.ToString("N"),
            });

    private static Task<int> SeedEmissionAsync(NpgsqlConnection connection, Guid documentId, string status) =>
        connection.ExecuteAsync(
            """
            INSERT INTO pipeline.b2c_margin_emissions
                (document_id, source_reference, aggregate_date, currency, category, role, content_hash, status)
            VALUES
                (@documentId, @sourceReference, DATE '2026-01-20', 'EUR', 'Tma1', 'Seller', @contentHash, @status)
            """,
            new
            {
                documentId,
                sourceReference = "ba-" + documentId.ToString("N"),
                contentHash = "chash-" + documentId.ToString("N"),
                status,
            });

    private static Task<string?> StateOfAsync(NpgsqlConnection connection, Guid id) =>
        connection.ExecuteScalarAsync<string?>(
            "SELECT state FROM documents.documents WHERE id = @id",
            new { id });

    private static void RunCommonMigrations(string connectionString)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance).MigrateUp();
    }

    private static DatabaseUpgradeResult RunAssemblyMigrations(string connectionString, Assembly assembly, Func<int, bool> versionFilter)
    {
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
}
