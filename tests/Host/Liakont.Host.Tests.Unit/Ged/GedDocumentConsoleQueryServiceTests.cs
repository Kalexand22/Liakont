namespace Liakont.Host.Tests.Unit.Ged;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Ged;
using Liakont.Host.Security;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Ged.Contracts.Consultation;
using Liakont.Modules.Ged.Contracts.Queries;
using Stratum.Common.Abstractions.Security;
using Xunit;

// Assemblage de la fiche document GED (GED09b) : orchestration du port de lecture GED, de la surface de coffre
// (intégrité re-lue + aperçu) et du journal de consultation. On couvre : le passage du droit confidentiel au
// masquage server-side, la trace view_document, le mapping d'intégrité GED-seul vs fiscal-lié, et le not-found.
public sealed class GedDocumentConsoleQueryServiceTests
{
    private static readonly Guid DocId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    [Fact]
    public async Task Returns_Null_And_Does_Not_Log_When_Document_Is_Not_Found()
    {
        var queries = new FakeGedDocumentQueries(document: null);
        var reader = new FakeManagedArchiveReader();
        var consultation = new FakeConsultationAuditWriter();
        var service = Build(queries, reader, consultation, permissions: GrantAll());

        var result = await service.GetAsync(DocId);

        result.Should().BeNull();
        consultation.Entries.Should().BeEmpty("aucun document lu → aucune consultation à journaliser");
        reader.VerifyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Passes_Confidential_Right_To_The_Query_And_Logs_A_View_Document_Consultation()
    {
        var queries = new FakeGedDocumentQueries(GedOnlyDocument());
        var reader = new FakeManagedArchiveReader(
            integrity: new GedArchiveIntegrityResult(GedArchiveIntegrityStatus.Verified, "hash", "hash", null),
            readableHtml: "<p>aperçu</p>");
        var consultation = new FakeConsultationAuditWriter();

        // Acteur AVEC le droit confidentiel.
        var service = Build(queries, reader, consultation, permissions: GrantAll());

        var result = await service.GetAsync(DocId);

        result.Should().NotBeNull();
        queries.LastHasConfidentialRight.Should().BeTrue("le droit confidentiel de l'acteur pilote le masquage server-side");

        consultation.Entries.Should().ContainSingle();
        var entry = consultation.Entries[0];
        entry.Action.Should().Be(ConsultationAction.ViewDocument);
        entry.ManagedDocumentId.Should().Be(DocId);
        entry.ActorHasConfidentialAccess.Should().BeTrue();

        result!.Integrity.State.Should().Be(GedDocumentIntegrityState.Verified);
        result.PreviewHtml.Should().Contain("aperçu");
    }

    [Fact]
    public async Task Masks_Confidential_When_The_Actor_Lacks_The_Right()
    {
        var queries = new FakeGedDocumentQueries(GedOnlyDocument());
        var reader = new FakeManagedArchiveReader();
        var consultation = new FakeConsultationAuditWriter();

        // Acteur SANS le droit confidentiel (seulement lecture GED).
        var service = Build(queries, reader, consultation, permissions: Grant(LiakontPermissions.GedRead));

        await service.GetAsync(DocId);

        queries.LastHasConfidentialRight.Should().BeFalse("sans liakont.ged.confidential, le masquage exclut les axes/entités confidentiels");
        consultation.Entries[0].ActorHasConfidentialAccess.Should().BeFalse();
    }

    [Fact]
    public async Task Uses_Fiscal_Integrity_And_Does_Not_Re_Read_The_Ged_Package_For_A_Fiscal_Linked_Document()
    {
        var fiscalId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
        var archiveEntryId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
        var queries = new FakeGedDocumentQueries(GedOnlyDocument() with
        {
            FiscalDocumentId = fiscalId,
            ArchiveEntryId = archiveEntryId,
        });
        var reader = new FakeManagedArchiveReader();
        var consultation = new FakeConsultationAuditWriter();
        var service = Build(queries, reader, consultation, permissions: GrantAll());

        var result = await service.GetAsync(DocId);

        result!.IsFiscalLinked.Should().BeTrue();
        result.FiscalDocumentId.Should().Be(fiscalId);
        result.Integrity.State.Should().Be(GedDocumentIntegrityState.FiscalLinked);
        result.PreviewHtml.Should().BeNull();
        reader.VerifyCalls.Should().Be(0, "l'intégrité fiscale est portée par le coffre fiscal, pas par la re-lecture GED");
        reader.ReadableCalls.Should().Be(0);
    }

    [Fact]
    public async Task Maps_Missing_Integrity_From_The_Archive_Reader()
    {
        var queries = new FakeGedDocumentQueries(GedOnlyDocument());
        var reader = new FakeManagedArchiveReader(
            integrity: new GedArchiveIntegrityResult(GedArchiveIntegrityStatus.Missing, "hash", null, "introuvable"));
        var consultation = new FakeConsultationAuditWriter();
        var service = Build(queries, reader, consultation, permissions: GrantAll());

        var result = await service.GetAsync(DocId);

        result!.Integrity.State.Should().Be(GedDocumentIntegrityState.Missing);
        result.Integrity.Detail.Should().Contain("introuvable");
        reader.VerifyCalls.Should().Be(1);
    }

    [Fact]
    public async Task Propagates_When_The_Consultation_Writer_Fails_In_Evidential_Mode()
    {
        var queries = new FakeGedDocumentQueries(GedOnlyDocument());
        var reader = new FakeManagedArchiveReader();
        var consultation = new FakeConsultationAuditWriter(throwOnWrite: true);
        var service = Build(queries, reader, consultation, permissions: GrantAll());

        // Régime probant : une trace en échec LÈVE → la page traduit en refus d'accès (fail-closed §6.6).
        var act = async () => await service.GetAsync(DocId);
        await act.Should().ThrowAsync<ConsultationAuditException>();
    }

    private static GedDocumentConsoleQueryService Build(
        IGedDocumentQueries queries,
        IManagedArchiveReader reader,
        IConsultationAuditWriter consultation,
        IPermissionService permissions) => new(queries, reader, consultation, permissions);

    private static GedManagedDocumentView GedOnlyDocument() => new()
    {
        Id = DocId,
        Title = "Bordereau acheteur 42",
        DocKind = "bordereau",
        Status = "indexed",
        RetentionClass = "tenant_bounded",
        ArchivePath = "_ged/documents/2026/06/bordereau-42/manifest.json",
        ContentHash = "hash",
        CreatedUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        Axes = [],
        Entities = [],
    };

    private static FakePermissionService GrantAll() =>
        Grant(LiakontPermissions.GedRead, LiakontPermissions.GedExport, LiakontPermissions.GedConfidential);

    private static FakePermissionService Grant(params string[] permissions) => new(permissions);

    private sealed class FakeGedDocumentQueries(GedManagedDocumentView? document) : IGedDocumentQueries
    {
        public bool LastHasConfidentialRight { get; private set; }

        public Task<GedManagedDocumentView?> GetAsync(Guid managedDocumentId, bool hasConfidentialRight, CancellationToken cancellationToken = default)
        {
            LastHasConfidentialRight = hasConfidentialRight;
            return Task.FromResult(document);
        }
    }

    private sealed class FakeManagedArchiveReader(
        GedArchiveIntegrityResult? integrity = null,
        string? readableHtml = null) : IManagedArchiveReader
    {
        public int VerifyCalls { get; private set; }

        public int ReadableCalls { get; private set; }

        public Task<GedArchiveIntegrityResult> VerifyManagedPackageAsync(string? manifestPath, string? indexedContentHash, CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            return Task.FromResult(integrity
                ?? new GedArchiveIntegrityResult(GedArchiveIntegrityStatus.NotArchived, indexedContentHash, null, null));
        }

        public Task<string?> ReadManagedReadableHtmlAsync(string? manifestPath, CancellationToken cancellationToken = default)
        {
            ReadableCalls++;
            return Task.FromResult(readableHtml);
        }
    }

    private sealed class FakeConsultationAuditWriter(bool throwOnWrite = false) : IConsultationAuditWriter
    {
        public List<ConsultationLogEntry> Entries { get; } = [];

        public Task WriteAsync(ConsultationLogEntry entry, CancellationToken cancellationToken = default)
        {
            if (throwOnWrite)
            {
                throw new ConsultationAuditException(
                    "Trace de consultation en échec (régime probant).",
                    new InvalidOperationException("écriture du journal impossible"));
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePermissionService(params string[] granted) : IPermissionService
    {
        private readonly HashSet<string> _granted = new(granted, StringComparer.Ordinal);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _granted.Contains(permission);
    }
}
