namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;

/// <summary>Double en mémoire d'<see cref="IDocumentQueries"/> pour les tests d'export contrôle fiscal (TRK06).</summary>
internal sealed class FakeDocumentQueries : IDocumentQueries
{
    private readonly Dictionary<Guid, DocumentDto> _documents = [];
    private readonly Dictionary<Guid, List<DocumentEventDto>> _events = [];

    public void Add(DocumentDto document, params DocumentEventDto[] events)
    {
        _documents[document.Id] = document;
        _events[document.Id] = events.ToList();
    }

    public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.TryGetValue(id, out DocumentDto? document) ? document : null);

    public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.Values.FirstOrDefault(d => d.DocumentNumber == documentNumber));

    public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<DocumentSummaryDto>)[]);

    public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<DocumentEventDto>)(_events.TryGetValue(documentId, out List<DocumentEventDto>? events) ? events : []));

    public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<DocumentSummaryDto>)[]);

    public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
        Task.FromResult<DocumentStatusDto?>(null);
}
