namespace Liakont.Modules.Signature.Tests.Integration.Fixtures;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;

/// <summary>Stub de <see cref="IDocumentQueries"/> pour les tests d'intégration du proxy : seul GetById est piloté.</summary>
internal sealed class StubDocumentQueries : IDocumentQueries
{
    private readonly DocumentDto? _document;

    public StubDocumentQueries(DocumentDto? document) => _document = document;

    public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_document is not null && _document.Id == id ? _document : null);

    public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
