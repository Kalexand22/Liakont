namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Handler de <c>GET /api/v1/documents/{id}/piece-jointe</c> (consultation du PDF d'origine reçu de
/// l'agent), branche par branche : 404 français sans tenant résolu / document introuvable (l'isolation
/// tenant est portée par la connexion de <c>IDocumentQueries</c>) / document sans PDF (cas normal), et
/// flux <c>application/pdf</c> servi <c>inline</c> avec un nom de fichier assaini (anti-injection
/// d'en-tête). L'enregistrement de la route sous <c>/api/v1</c> et la garde <c>liakont.read</c> (403)
/// sont couverts côté HTTP par <c>Liakont.Console.Api.Tests.Integration.DocumentSourcePdfEndpointIntegrationTests</c>.
/// </summary>
public sealed class DocumentSourcePdfEndpointTests
{
    private static readonly Guid DocId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Returns_404_When_No_Tenant_Is_Resolved()
    {
        var result = await DocumentSourcePdfEndpoint.HandleAsync(
            DocId,
            new FakeDocumentQueries { Document = Doc("100352") },
            new FakeLinkedPdfReadStore(),
            new FakeTenantContext(null),
            new DefaultHttpContext());

        result.Should().BeOfType<NotFound<string>>()
            .Which.Value.Should().Contain("tenant", "sans tenant résolu, aucun stockage n'est adressable");
    }

    [Fact]
    public async Task Returns_404_When_The_Document_Is_Unknown_On_This_Tenant()
    {
        // L'isolation cross-tenant est portée par la lecture tenant-scopée : l'identifiant d'un document
        // d'un AUTRE tenant est simplement introuvable ici (la connexion EST le tenant).
        var store = new FakeLinkedPdfReadStore();

        var result = await DocumentSourcePdfEndpoint.HandleAsync(
            DocId,
            new FakeDocumentQueries { Document = null },
            store,
            new FakeTenantContext("tenant-demo"),
            new DefaultHttpContext());

        result.Should().BeOfType<NotFound<string>>().Which.Value.Should().Contain("introuvable");
        store.Opens.Should().BeEmpty("le stockage n'est pas consulté pour un document inconnu du tenant");
    }

    [Fact]
    public async Task Returns_404_With_The_Document_Number_When_No_Pdf_Was_Ingested()
    {
        var result = await DocumentSourcePdfEndpoint.HandleAsync(
            DocId,
            new FakeDocumentQueries { Document = Doc("100353") },
            new FakeLinkedPdfReadStore { Content = null },
            new FakeTenantContext("tenant-demo"),
            new DefaultHttpContext());

        result.Should().BeOfType<NotFound<string>>()
            .Which.Value.Should().Contain("100353", "le message opérateur porte le n° du document (CLAUDE.md n°12)");
    }

    [Fact]
    public async Task Streams_The_Pdf_Inline_With_A_Readable_File_Name()
    {
        var context = new DefaultHttpContext();
        var store = new FakeLinkedPdfReadStore { Content = Encoding.UTF8.GetBytes("%PDF-fake") };

        var result = await DocumentSourcePdfEndpoint.HandleAsync(
            DocId,
            new FakeDocumentQueries { Document = Doc("100352") },
            store,
            new FakeTenantContext("tenant-demo"),
            context);

        var file = result.Should().BeOfType<FileStreamHttpResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        context.Response.Headers.ContentDisposition.ToString()
            .Should().Be("inline; filename=\"100352.pdf\"", "consultation dans le navigateur, nom lisible (jamais le hash de stockage)");
        context.Response.Headers.XContentTypeOptions.ToString()
            .Should().Be("nosniff", "un navigateur ne re-devine jamais le type d'un fichier poussé par l'agent (défense en profondeur)");
        store.Opens.Should().ContainSingle().Which.Should().Be(
            ("tenant-demo", "encheresv6:ba:100352"),
            "le flux est adressé par le tenant courant et la référence source du document");
    }

    [Fact]
    public async Task Sanitizes_The_Document_Number_In_The_Content_Disposition_Header()
    {
        // Anti-injection d'en-tête : un n° de document exotique (guillemets, CR/LF, séparateurs) est
        // réduit au jeu sûr — jamais recopié tel quel dans Content-Disposition.
        var context = new DefaultHttpContext();

        var result = await DocumentSourcePdfEndpoint.HandleAsync(
            DocId,
            new FakeDocumentQueries { Document = Doc("BA\"\r\n/100 352") },
            new FakeLinkedPdfReadStore { Content = [0x25] },
            new FakeTenantContext("tenant-demo"),
            context);

        result.Should().BeOfType<FileStreamHttpResult>();
        context.Response.Headers.ContentDisposition.ToString()
            .Should().Be("inline; filename=\"BA----100-352.pdf\"");
    }

    private static DocumentDto Doc(string number) => new()
    {
        Id = DocId,
        SourceReference = $"encheresv6:ba:{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        SupplierSiren = "123456782",
        CustomerName = "DUPONT J.",
        CustomerIsCompanyHint = false,
        TotalNet = 1000m,
        TotalTax = 162.80m,
        TotalGross = 1162.80m,
        State = "Issued",
        PayloadHash = "sha256:payload",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        public DocumentDto? Document { get; init; }

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Document);

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Fake du stockage : seule l'ouverture du PDF rattaché est utilisée par le handler. Enregistre
    // (tenant, référence) pour prouver l'adressage.
    private sealed class FakeLinkedPdfReadStore : IIngestedPdfStore
    {
        public byte[]? Content { get; init; }

        public List<(string TenantId, string SourceReference)> Opens { get; } = [];

        public Task<Stream?> TryOpenLinkedPdfAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default)
        {
            Opens.Add((tenantId, sourceReference));
            return Task.FromResult<Stream?>(Content is null ? null : new MemoryStream(Content, writable: false));
        }

        public Task<bool> LinkedPdfExistsAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SaveLinkedPdfAsync(string tenantId, string sourceReference, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SavePooledPdfAsync(string tenantId, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PooledPdfReference>> ListPooledPdfsAsync(string tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream> OpenPooledPdfAsync(string tenantId, string poolPdfId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }
}
