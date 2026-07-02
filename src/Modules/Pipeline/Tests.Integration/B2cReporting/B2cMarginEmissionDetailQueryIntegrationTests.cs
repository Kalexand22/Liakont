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

    [Fact]
    public async Task GetEmissionBatchIdForDocument_Resolves_The_Batch_Of_An_Issued_Document_Without_Any_Event()
    {
        // BUG-24 / ADR-0037 §4 : le lien « Voir la déclaration » lit le lot depuis le JOURNAL D'ÉMISSION (source de
        // vérité de la liaison), pas depuis un événement d'audit. Ce test prouve la RÉTROACTIVITÉ : le lot se résout
        // à partir des SEULES entrées du journal — exactement l'état d'un document rétro-corrigé par V012, qui n'a
        // AUCUN événement DocumentEReported. On n'écrit ici que dans pipeline.b2c_margin_emissions (aucun événement).
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresB2cMarginEmissionStore(factory);
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        var batchId = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var date = new DateOnly(2099, 8, 12);

        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000010", batchId, date, B2cMarginEmissionStatus.Pending));
        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000010", batchId, date, B2cMarginEmissionStatus.Issued, paEmissionId: "610"));

        (await queries.GetEmissionBatchIdForDocumentAsync(doc)).Should().Be(batchId,
            "la liaison document→lot vit dans le journal, indépendamment de tout événement (cas backfill V012)");
    }

    [Fact]
    public async Task GetEmissionBatchIdForDocument_Returns_The_Most_Recent_Issued_Batch()
    {
        // Un document tardif ré-agrégé (D3) peut appartenir à DEUX transmissions Issued : on retourne la PLUS RÉCENTE
        // (created_utc, seq décroissants) — le lot que reflète l'état EReported courant. Déterminisme garanti par seq.
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresB2cMarginEmissionStore(factory);
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        var firstBatch = Guid.NewGuid();
        var secondBatch = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var date = new DateOnly(2099, 8, 13);

        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000011", firstBatch, date, B2cMarginEmissionStatus.Issued, paEmissionId: "611"));
        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000011", secondBatch, date, B2cMarginEmissionStatus.Issued, paEmissionId: "612"));

        (await queries.GetEmissionBatchIdForDocumentAsync(doc)).Should().Be(secondBatch, "le lot le plus récent l'emporte");
    }

    [Fact]
    public async Task GetEmissionBatchIdForDocument_Returns_Null_When_The_Document_Has_No_Issued_Emission()
    {
        // Aucune issue confirmée (seulement Pending) → aucun lot (le document n'est pas réellement e-reporté).
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresB2cMarginEmissionStore(factory);
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        var batchId = Guid.NewGuid();
        var doc = Guid.NewGuid();

        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000012", batchId, new DateOnly(2099, 8, 14), B2cMarginEmissionStatus.Pending));

        (await queries.GetEmissionBatchIdForDocumentAsync(doc)).Should().BeNull("aucune entrée Issued ⇒ aucun lot résolu");
        (await queries.GetEmissionBatchIdForDocumentAsync(Guid.NewGuid())).Should().BeNull("document inconnu ⇒ null");
    }

    [Fact]
    public async Task GetEmissionBatchIdForDocument_Returns_Null_When_The_Only_Attempt_Was_Rejected()
    {
        // Couverture réintroduite (GDF02) : une émission TENTÉE puis REJETÉE par la PA (Pending → RejectedByPa)
        // ne résout AUCUN lot de déclaration — le document n'est PAS e-reporté (aucune entrée Issued). L'ancien
        // test …Was_Only_Attempted_Or_Rejected avait été remplacé par un seed Pending SEUL, perdant le cas rejet.
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresB2cMarginEmissionStore(factory);
        var queries = new PostgresB2cMarginEmissionQueries(factory);

        var batchId = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var date = new DateOnly(2099, 8, 15);

        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000013", batchId, date, B2cMarginEmissionStatus.Pending));
        await store.AppendAsync(Entry(doc, "encheresv6:ba:9000013", batchId, date, B2cMarginEmissionStatus.RejectedByPa,
            detail: "[SPDP_B2C_REJECTED] Rejet de l'agrégat."));

        (await queries.GetEmissionBatchIdForDocumentAsync(doc)).Should().BeNull(
            "une émission rejetée n'e-reporte pas le document (aucune entrée Issued ⇒ aucun lot résolu).");
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
