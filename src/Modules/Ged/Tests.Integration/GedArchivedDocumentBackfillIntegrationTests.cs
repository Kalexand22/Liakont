namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Contracts.Backfill;
using Liakont.Modules.Ged.Domain.Mapping;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Backfill;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Infrastructure.Mapping;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration base RÉELLE (Testcontainers PostgreSQL) du backfill rétroactif GED du corpus fiscal déjà
/// scellé (GED10, F19 §11 D12). Prouvent : (1) un document dont le type N'A PAS de profil validé est DÉFÉRÉ (jamais
/// deviné, règle 3), avec les soft-links posés (archive_entry_id/fiscal_document_id/archive_path/content_hash) ;
/// (2) l'IDEMPOTENCE — un re-passage sur la même entrée de coffre est un no-op (identité GED déterministe, RL-21) ;
/// (3) un document dont le type A un profil validé est INDEXÉ (liens d'axe écrits, source « import »).
/// </summary>
[Collection("GedIntegration")]
public sealed class GedArchivedDocumentBackfillIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public GedArchivedDocumentBackfillIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Backfill_without_profile_defers_with_softlinks_and_rerun_is_a_no_op()
    {
        var factory = _fixture.CreateTenantDatabase();
        var backfill = BuildBackfill(factory);

        var archiveEntryId = Guid.NewGuid();
        var fiscalDocumentId = Guid.NewGuid();
        var request = new GedBackfillDocumentRequest(
            ArchiveEntryId: archiveEntryId,
            FiscalDocumentId: fiscalDocumentId,
            ArchivePath: "2026/06/FAC-1",
            ContentHash: "0f1e2d3c",
            DocumentType: "facture",
            SourceReference: "FAC-1",
            SourceFields: new Dictionary<string, string> { ["documentNumber"] = "FAC-1" });

        var first = await backfill.BackfillAsync(request, CancellationToken.None);

        first.Should().Be(GedBackfillOutcome.Deferred, "aucun profil GED ne couvre le type fiscal « facture » — DEFER, jamais deviner");

        var managedDocumentId = GedDeterministicId.ForArchiveEntry(archiveEntryId);
        var row = await ReadManagedDocumentAsync(factory, managedDocumentId);
        row.Should().NotBeNull("l'identité GED est dérivée DÉTERMINISTEMENT de l'archive_entry_id");
        row!.Status.Should().Be("deferred");
        row.DeferReason.Should().NotBeNullOrWhiteSpace("un déférement porte un motif français actionnable (n°12)");
        row.ArchiveEntryId.Should().Be(archiveEntryId, "le soft-link vers l'entrée de coffre est posé (clé d'idempotence)");
        row.FiscalDocumentId.Should().Be(fiscalDocumentId, "le soft-link vers le document fiscal est posé");
        row.ArchivePath.Should().Be("2026/06/FAC-1");
        row.ContentHash.Should().Be("0f1e2d3c", "l'empreinte est recopiée du coffre, jamais recalculée");

        // Re-passage sur la MÊME entrée : idempotent (RL-21) — aucune seconde ligne, aucun changement.
        var second = await backfill.BackfillAsync(request, CancellationToken.None);

        second.Should().Be(GedBackfillOutcome.AlreadyPresent);
        (await CountManagedDocumentsAsync(factory)).Should().Be(1, "un re-backfill ne crée pas de doublon (idempotence par identité déterministe)");
    }

    [Fact]
    public async Task Backfill_with_validated_profile_indexes_axis_with_import_source()
    {
        var factory = _fixture.CreateTenantDatabase();
        var backfill = BuildBackfill(factory);

        await SeedAxisAsync(factory, "reference", "string");
        await SeedValidatedProfileAsync(factory, "note_interne", "reference", "$.fields.reference");

        var archiveEntryId = Guid.NewGuid();
        var request = new GedBackfillDocumentRequest(
            ArchiveEntryId: archiveEntryId,
            FiscalDocumentId: Guid.NewGuid(),
            ArchivePath: "2026/06/N-1",
            ContentHash: "aa11bb22",
            DocumentType: "note_interne",
            SourceReference: "N-1",
            SourceFields: new Dictionary<string, string> { ["reference"] = "REF-9" });

        var outcome = await backfill.BackfillAsync(request, CancellationToken.None);

        outcome.Should().Be(GedBackfillOutcome.Indexed);

        var managedDocumentId = GedDeterministicId.ForArchiveEntry(archiveEntryId);
        (await StatusAsync(factory, managedDocumentId)).Should().Be("indexed");

        var link = await ReadSingleAxisLinkAsync(factory);
        link.ValueString.Should().Be("REF-9", "l'axe est mappé depuis le champ source");
        link.Source.Should().Be("import", "le backfill écrit ses liens avec la provenance « import » (jamais « agent »)");

        (await backfill.BackfillAsync(request, CancellationToken.None)).Should().Be(GedBackfillOutcome.AlreadyPresent);
        (await CountManagedDocumentsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Rerun_after_profile_validation_resumes_deferred_document_to_indexed_with_audit_trail()
    {
        var factory = _fixture.CreateTenantDatabase();
        var backfill = BuildBackfill(factory);

        await SeedAxisAsync(factory, "reference", "string");

        var archiveEntryId = Guid.NewGuid();
        var request = new GedBackfillDocumentRequest(
            ArchiveEntryId: archiveEntryId,
            FiscalDocumentId: Guid.NewGuid(),
            ArchivePath: "2026/06/N-2",
            ContentHash: "cc33dd44",
            DocumentType: "note_interne",
            SourceReference: "N-2",
            SourceFields: new Dictionary<string, string> { ["reference"] = "REF-42" });

        // 1er passage : aucun profil ne couvre « note_interne » → DÉFÉRÉ (jamais deviner, règle 3).
        (await backfill.BackfillAsync(request, CancellationToken.None)).Should().Be(GedBackfillOutcome.Deferred);

        var managedDocumentId = GedDeterministicId.ForArchiveEntry(archiveEntryId);
        (await StatusAsync(factory, managedDocumentId)).Should().Be("deferred");

        // L'opérateur crée+valide un profil (action prescrite par le motif de déférement), puis RELANCE le backfill.
        await SeedValidatedProfileAsync(factory, "note_interne", "reference", "$.fields.reference");

        var rerun = await backfill.BackfillAsync(request, CancellationToken.None);

        // Reprise ciblée (GDF10) : le déféré devenu mappable est PROMU deferred→indexed — l'action prescrite a un EFFET.
        rerun.Should().Be(GedBackfillOutcome.Indexed, "un re-run reprend un document déféré devenu mappable (profil validé depuis)");
        (await StatusAsync(factory, managedDocumentId)).Should().Be("indexed");
        (await CountManagedDocumentsAsync(factory)).Should().Be(1, "la reprise MUTE la fiche existante (V008 mutable), sans doublon");

        var row = await ReadManagedDocumentAsync(factory, managedDocumentId);
        row!.DeferReason.Should().BeNull("un document promu n'est plus en attente");
        row.ArchiveEntryId.Should().Be(archiveEntryId, "les soft-links du coffre sont conservés à la promotion");

        var link = await ReadSingleAxisLinkAsync(factory);
        link.ValueString.Should().Be("REF-42", "l'axe est mappé depuis le champ source lors de la reprise");
        link.Source.Should().Be("import", "la reprise écrit ses liens avec la provenance « import » (jamais « agent »)");

        // Mutation de statut TRACÉE dans le journal append-only (auditabilité, CLAUDE.md n°4 / INV-GED-02).
        (await CountStatusChangedLogAsync(factory, managedDocumentId)).Should().Be(1, "la promotion deferred→indexed est tracée une fois");

        // Nouveau re-run : le document est désormais indexé → no-op TERMINAL (idempotence conservée), aucune 2e trace.
        (await backfill.BackfillAsync(request, CancellationToken.None)).Should().Be(GedBackfillOutcome.AlreadyPresent, "un indexed reste no-op au replay");
        (await StatusAsync(factory, managedDocumentId)).Should().Be("indexed");
        (await CountStatusChangedLogAsync(factory, managedDocumentId)).Should().Be(1, "aucun status_changed fictif : un indexed ne re-promeut pas");
    }

    private static GedArchivedDocumentBackfill BuildBackfill(IConnectionFactory factory)
    {
        var indexer = new GedDocumentIndexer(
            new GedMappingProfileRepository(factory),
            new PostgresAxisCatalog(factory),
            new PostgresEntityCatalog(factory),
            new PostgresGedIndexUnitOfWorkFactory(factory),
            NullLogger<GedDocumentIndexer>.Instance);
        return new GedArchivedDocumentBackfill(indexer);
    }

    private static async Task SeedAxisAsync(IConnectionFactory factory, string code, string dataType)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_catalog.axis_definitions (code, label, data_type, is_multi_value, is_active)
            VALUES (@Code, @Label, @DataType, false, true)
            """,
            new { Code = code, Label = code, DataType = dataType });
    }

    private static async Task SeedValidatedProfileAsync(IConnectionFactory factory, string documentType, string axisCode, string selector)
    {
        var repository = new GedMappingProfileRepository(factory);
        var profile = GedMappingProfile.Create(
            documentType,
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: "WormPlusIndex",
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule(axisCode, selector, IsRequired: true, IsMulti: false) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);

        await repository.InsertProfileAsync(profile, GedMappingChangeLogFactory.ForCreateProfile(profile, "ec@example.test", "Expert"));
    }

    private static async Task<ManagedDocumentRow?> ReadManagedDocumentAsync(IConnectionFactory factory, Guid id)
    {
        using var connection = await factory.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<ManagedDocumentRow>(
            """
            SELECT status AS Status, defer_reason AS DeferReason, fiscal_document_id AS FiscalDocumentId,
                   archive_entry_id AS ArchiveEntryId, archive_path AS ArchivePath, content_hash AS ContentHash
            FROM ged_index.managed_documents WHERE id = @Id
            """,
            new { Id = id });
    }

    private static async Task<string?> StatusAsync(IConnectionFactory factory, Guid id)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>("SELECT status FROM ged_index.managed_documents WHERE id = @Id", new { Id = id });
    }

    private static async Task<AxisLinkRow> ReadSingleAxisLinkAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.QuerySingleAsync<AxisLinkRow>(
            "SELECT value_string AS ValueString, source AS Source FROM ged_index.document_axis_links LIMIT 1");
    }

    private static async Task<long> CountManagedDocumentsAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.managed_documents");
    }

    private static async Task<long> CountStatusChangedLogAsync(IConnectionFactory factory, Guid managedDocumentId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            """
            SELECT count(*) FROM ged_index.managed_document_change_log
            WHERE managed_document_id = @Id AND change_type = 'status_changed'
            """,
            new { Id = managedDocumentId });
    }

    private sealed record ManagedDocumentRow
    {
        public string Status { get; init; } = string.Empty;

        public string? DeferReason { get; init; }

        public Guid? FiscalDocumentId { get; init; }

        public Guid? ArchiveEntryId { get; init; }

        public string? ArchivePath { get; init; }

        public string? ContentHash { get; init; }
    }

    private sealed record AxisLinkRow
    {
        public string? ValueString { get; init; }

        public string Source { get; init; } = string.Empty;
    }
}
