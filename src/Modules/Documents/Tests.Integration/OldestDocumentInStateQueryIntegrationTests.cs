namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Xunit;

/// <summary>
/// Lecture <c>GetOldestDocumentInStateAsync</c> (ajout SUP01b) sur PostgreSQL réel : retourne le document
/// le PLUS ANCIEN (plus petit <c>last_update_utc</c>) d'un état, ou <c>null</c> si l'état est vide. Chaque
/// test utilise un état UNIQUE (chaîne aléatoire) pour être robuste à la fixture partagée — le tri et la
/// borne à une ligne sont prouvés sans dépendre des documents seedés par les autres tests.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class OldestDocumentInStateQueryIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public OldestDocumentInStateQueryIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetOldestDocumentInStateAsync_Returns_The_Oldest_By_LastUpdate()
    {
        var harness = new DocumentsHarness(_fixture);
        var state = "SUP01B_OLDEST_" + Guid.NewGuid().ToString("N");

        var newer = await InsertAsync(harness, state, new DateTimeOffset(2001, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var oldest = await InsertAsync(harness, state, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await harness.Queries.GetOldestDocumentInStateAsync(state);

        result.Should().NotBeNull();
        result!.Id.Should().Be(oldest, "la lecture retourne le plus petit last_update_utc, pas le plus récent.");
        result.Id.Should().NotBe(newer);
        result.State.Should().Be(state);
    }

    [Fact]
    public async Task GetOldestDocumentInStateAsync_Returns_Null_When_State_Is_Empty()
    {
        var harness = new DocumentsHarness(_fixture);
        var emptyState = "SUP01B_EMPTY_" + Guid.NewGuid().ToString("N");

        var result = await harness.Queries.GetOldestDocumentInStateAsync(emptyState);

        result.Should().BeNull();
    }

    private static async Task<Guid> InsertAsync(DocumentsHarness harness, string state, DateTimeOffset lastUpdateUtc)
    {
        var id = Guid.NewGuid();
        var suffix = id.ToString("N");

        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 total_net, total_tax, total_gross, state, payload_hash, first_seen_utc, last_update_utc)
            VALUES
                (@Id, @Src, @Num, 'Invoice', @IssueDate,
                 0, 0, 0, @State, @Hash, @Ts, @Ts)
            """,
            new
            {
                Id = id,
                Src = "SRC-" + suffix,
                Num = "NUM-" + suffix,
                IssueDate = new DateOnly(2026, 1, 1),
                State = state,
                Hash = "hash-" + suffix,
                Ts = lastUpdateUtc,
            });

        return id;
    }
}
