namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.Pipeline.Infrastructure.Persistence;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Liakont.Modules.Pipeline.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Détail d'une transmission e-reporting B2C (BUG-22, <c>GetEmissionDetailAsync</c>) bout en bout sur PostgreSQL
/// réel : prouve que la query retourne l'ÉTAT COURANT du lot (dernière entrée — Pending puis Issued → Issued),
/// le snapshot brut de réponse PA, et la liste des PIÈCES distinctes (un document = plusieurs entrées) regroupées
/// par <c>emission_batch_id</c>. Base PARTAGÉE : chaque test utilise un <c>emission_batch_id</c> NEUF (filtre par
/// lot) — aucune dépendance à l'ordre des tests. Journal append-only : on n'écrit que des INSERT (via le store).
/// </summary>
[Collection("PipelineIntegration")]
public sealed class B2cMarginEmissionDetailQueryIntegrationTests
{
    private readonly PipelineDatabaseFixture _fixture;

    public B2cMarginEmissionDetailQueryIntegrationTests(PipelineDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetEmissionDetail_Returns_Current_Status_Pa_Snapshot_And_Distinct_Documents()
    {
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresB2cMarginEmissionStore(factory);
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        var batchId = Guid.NewGuid();
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        var date = new DateOnly(2099, 6, 23);

        // Attempt-once : une entrée Pending AVANT le POST, puis l'issue (Issued) après — par document.
        await store.AppendAsync(Entry(doc1, "encheresv6:ba:9000004", batchId, date, B2cMarginEmissionStatus.Pending));
        await store.AppendAsync(Entry(doc2, "encheresv6:ba:9000005", batchId, date, B2cMarginEmissionStatus.Pending));
        await store.AppendAsync(Entry(doc1, "encheresv6:ba:9000004", batchId, date, B2cMarginEmissionStatus.Issued, paEmissionId: "591"));
        await store.AppendAsync(Entry(doc2, "encheresv6:ba:9000005", batchId, date, B2cMarginEmissionStatus.Issued, paEmissionId: "591"));

        var detail = await queries.GetEmissionDetailAsync(batchId);

        detail.Should().NotBeNull();
        detail!.Status.Should().Be("Issued", "l'état courant est la dernière entrée du lot");
        detail.PaEmissionId.Should().Be("591");
        detail.AggregateDate.Should().Be(date);
        detail.Documents.Should().HaveCount(2, "deux pièces distinctes composent l'agrégat (chacune a plusieurs entrées)");
        detail.Documents.Select(d => d.SourceReference).Should().Contain(["encheresv6:ba:9000004", "encheresv6:ba:9000005"]);
    }

    [Fact]
    public async Task GetEmissionDetail_Surfaces_The_Raw_Pa_Response_Snapshot_Of_A_Rejection()
    {
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresB2cMarginEmissionStore(factory);
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        var batchId = Guid.NewGuid();
        var doc = Guid.NewGuid();
        const string snapshot = """{"http_status_code":400,"message":"cannot add transaction at date 2024-01-03"}""";

        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000006", batchId, new DateOnly(2099, 7, 1), B2cMarginEmissionStatus.Pending));
        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000006", batchId, new DateOnly(2099, 7, 1), B2cMarginEmissionStatus.RejectedByPa,
            paResponseSnapshot: snapshot, detail: "[SPDP_B2C_REJECTED] Rejet de l'agrégat."));

        var detail = await queries.GetEmissionDetailAsync(batchId);

        detail.Should().NotBeNull();
        detail!.Status.Should().Be("RejectedByPa");
        detail.PaResponseSnapshot.Should().Be(snapshot, "le snapshot brut est exposé pour restitution lisible côté console");
        detail.Detail.Should().Contain("Rejet de l'agrégat");
    }

    [Fact]
    public async Task GetEmissionDetail_Returns_Null_For_An_Unknown_Batch()
    {
        var factory = _fixture.CreateConnectionFactory();
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        (await queries.GetEmissionDetailAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static B2cMarginEmissionEntry Entry(
        Guid documentId,
        string sourceReference,
        Guid batchId,
        DateOnly date,
        B2cMarginEmissionStatus status,
        string? paEmissionId = null,
        string? paResponseSnapshot = null,
        string? detail = null) =>
        new()
        {
            DocumentId = documentId,
            SourceReference = sourceReference,
            AggregateDate = date,
            CurrencyCode = "EUR",
            Category = "TMA1",
            Role = "SE",
            ContentHash = "hash-" + batchId.ToString("N"),
            EmissionBatchId = batchId,
            Status = status,
            PaEmissionId = paEmissionId,
            PaResponseSnapshot = paResponseSnapshot,
            Detail = detail,
        };
}
