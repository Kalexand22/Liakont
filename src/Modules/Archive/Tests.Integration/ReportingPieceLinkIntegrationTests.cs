namespace Liakont.Modules.Archive.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Tests.Integration.Fixtures;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration de la traçabilité reporting↔pièces (item B2C03) sur PostgreSQL réel
/// (<c>documents.reporting_piece_links</c>, migration V011) : ajout idempotent, lecture DOUBLE SENS, garde
/// APPEND-ONLY (triggers anti UPDATE/DELETE/TRUNCATE) et tenant-scoping prouvé sur company_id ET sur DEUX
/// bases distinctes.
/// </summary>
[Collection("ArchiveIntegration")]
public sealed class ReportingPieceLinkIntegrationTests
{
    private static readonly Guid TenantA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly ArchiveDatabaseFixture _fixture;

    public ReportingPieceLinkIntegrationTests(ArchiveDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Append_ThenQuery_BothDirections()
    {
        var store = new PostgresReportingPieceLinkStore(_fixture.CreateTenantDatabase());
        var transmission = Guid.NewGuid();

        await store.AppendAsync(TenantA, transmission, ["BA-2026-1", "BV-2026-1"]);

        // Sens transmission → pièces.
        var byDocument = await store.GetByDocumentAsync(TenantA, transmission);
        byDocument.Select(l => l.SourceReference).Should().BeEquivalentTo("BA-2026-1", "BV-2026-1");
        byDocument.Should().OnlyContain(l => l.CompanyId == TenantA && l.DocumentId == transmission);

        // Sens pièce → transmissions.
        var bySource = await store.GetBySourceReferenceAsync(TenantA, "BA-2026-1");
        bySource.Select(l => l.DocumentId).Should().ContainSingle().Which.Should().Be(transmission);
    }

    [Fact]
    public async Task Append_IsIdempotent_OnReplay()
    {
        var store = new PostgresReportingPieceLinkStore(_fixture.CreateTenantDatabase());
        var transmission = Guid.NewGuid();

        await store.AppendAsync(TenantA, transmission, ["BA-2026-1"]);
        await store.AppendAsync(TenantA, transmission, ["BA-2026-1"]);

        var links = await store.GetByDocumentAsync(TenantA, transmission);
        links.Should().ContainSingle();
    }

    [Fact]
    public async Task Update_IsRejectedByAppendOnlyTrigger()
    {
        IConnectionFactory factory = _fixture.CreateTenantDatabase();
        var store = new PostgresReportingPieceLinkStore(factory);
        var transmission = Guid.NewGuid();
        ReportingPieceLink link = (await store.AppendAsync(TenantA, transmission, ["BA-2026-1"]))[0];

        using var connection = await factory.OpenAsync();
        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE documents.reporting_piece_links SET source_reference = 'altéré' WHERE id = @Id",
            new { Id = link.LinkId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Delete_IsRejectedByAppendOnlyTrigger()
    {
        IConnectionFactory factory = _fixture.CreateTenantDatabase();
        var store = new PostgresReportingPieceLinkStore(factory);
        var transmission = Guid.NewGuid();
        ReportingPieceLink link = (await store.AppendAsync(TenantA, transmission, ["BA-2026-1"]))[0];

        using var connection = await factory.OpenAsync();
        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM documents.reporting_piece_links WHERE id = @Id",
            new { Id = link.LinkId });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Truncate_IsRejectedByAppendOnlyTrigger()
    {
        IConnectionFactory factory = _fixture.CreateTenantDatabase();
        var store = new PostgresReportingPieceLinkStore(factory);
        await store.AppendAsync(TenantA, Guid.NewGuid(), ["BA-2026-1"]);

        using var connection = await factory.OpenAsync();
        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE documents.reporting_piece_links");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task CompanyFilter_IsolatesTenants_InSameDatabase()
    {
        // Défense en profondeur : même base, mais une lecture filtrée sur un AUTRE company_id ne voit rien.
        var store = new PostgresReportingPieceLinkStore(_fixture.CreateTenantDatabase());
        var transmission = Guid.NewGuid();
        await store.AppendAsync(TenantA, transmission, ["BA-2026-1"]);

        (await store.GetByDocumentAsync(TenantB, transmission)).Should().BeEmpty();
        (await store.GetBySourceReferenceAsync(TenantB, "BA-2026-1")).Should().BeEmpty();
    }

    [Fact]
    public async Task LinksAreScopedToTheirOwnDatabase_AcrossTwoTenants()
    {
        // Isolation forte : deux bases tenant distinctes (≥ 2 bases). Un lien écrit dans la base A n'existe
        // pas dans la base B (chacune sa propre table physique).
        var storeA = new PostgresReportingPieceLinkStore(_fixture.CreateTenantDatabase());
        IConnectionFactory dbB = _fixture.CreateTenantDatabase();
        var storeB = new PostgresReportingPieceLinkStore(dbB);
        var transmission = Guid.NewGuid();

        await storeA.AppendAsync(TenantA, transmission, ["BA-2026-1"]);

        (await storeB.GetByDocumentAsync(TenantA, transmission)).Should().BeEmpty();

        using var connectionB = await dbB.OpenAsync();
        long countB = await connectionB.QueryFirstAsync<long>("SELECT count(*) FROM documents.reporting_piece_links");
        countB.Should().Be(0);
    }
}
