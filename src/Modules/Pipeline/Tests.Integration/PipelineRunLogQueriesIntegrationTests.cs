namespace Liakont.Modules.Pipeline.Tests.Integration;

using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Liakont.Modules.Pipeline.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// La migration <c>pipeline.run_logs</c> s'applique sur PostgreSQL réel et le journal est relu fidèlement
/// par <see cref="PostgresPipelineRunQueries"/> (INV-PIPELINE-005/007). PIP01a n'écrit aucune exécution :
/// les lignes sont insérées en SQL brut (le writer arrive avec PIP01b+). La table est PARTAGÉE par la
/// collection : les assertions sont scopées par identifiant (jamais de comptage global — fixture partagée).
/// </summary>
[Collection("PipelineIntegration")]
public sealed class PipelineRunLogQueriesIntegrationTests
{
    private readonly PipelineDatabaseFixture _fixture;

    public PipelineRunLogQueriesIntegrationTests(PipelineDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Inserted_Run_Is_Read_Back_Faithfully()
    {
        var factory = _fixture.CreateConnectionFactory();
        var started = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var completed = started.AddMinutes(3);
        var id = Guid.NewGuid();

        using (var conn = await factory.OpenAsync())
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO pipeline.run_logs
                    (id, run_type, run_trigger, started_at, completed_at,
                     documents_processed, documents_succeeded, documents_failed, detail)
                VALUES
                    (@Id, @RunType, @Trigger, @StartedAt, @CompletedAt, @Processed, @Succeeded, @Failed, @Detail)
                """,
                new
                {
                    Id = id,
                    RunType = nameof(PipelineRunType.Check),
                    Trigger = nameof(PipelineRunTrigger.Scheduled),
                    StartedAt = started,
                    CompletedAt = completed,
                    Processed = 7,
                    Succeeded = 6,
                    Failed = 1,
                    Detail = "course de test",
                });
        }

        var queries = new PostgresPipelineRunQueries(factory);
        var runs = await queries.GetRecentRunsAsync(200);

        var run = runs.Should().ContainSingle(r => r.Id == id).Subject;
        run.RunType.Should().Be(PipelineRunType.Check);
        run.Trigger.Should().Be(PipelineRunTrigger.Scheduled);
        run.StartedAt.Should().Be(started);
        run.CompletedAt.Should().Be(completed);
        run.DocumentsProcessed.Should().Be(7);
        run.DocumentsSucceeded.Should().Be(6);
        run.DocumentsFailed.Should().Be(1);
        run.Detail.Should().Be("course de test");
    }

    [Fact]
    public async Task GetRecentRuns_Orders_By_Started_Descending()
    {
        var factory = _fixture.CreateConnectionFactory();

        // Horodatages volontairement très lointains : les deux lignes sont en tête du tri, robustes à la
        // pollution de la table partagée (les autres lignes sont plus anciennes).
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var baseInstant = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using (var conn = await factory.OpenAsync())
        {
            await InsertMinimalAsync(conn, older, baseInstant);
            await InsertMinimalAsync(conn, newer, baseInstant.AddDays(1));
        }

        var queries = new PostgresPipelineRunQueries(factory);
        var ids = (await queries.GetRecentRunsAsync(200)).Select(r => r.Id).ToList();

        ids.Should().Contain(new[] { newer, older });
        ids.Should().ContainInOrder(newer, older);
    }

    private static Task<int> InsertMinimalAsync(IDbConnection connection, Guid id, DateTimeOffset startedAt) =>
        connection.ExecuteAsync(
            """
            INSERT INTO pipeline.run_logs
                (id, run_type, run_trigger, started_at, documents_processed, documents_succeeded, documents_failed)
            VALUES (@Id, 'Send', 'Manual', @StartedAt, 0, 0, 0)
            """,
            new { Id = id, StartedAt = startedAt });
}
