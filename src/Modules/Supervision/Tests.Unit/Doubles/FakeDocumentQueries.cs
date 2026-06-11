namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;

/// <summary>
/// Requêtes Documents fictives pour la supervision : <see cref="GetOldestDocumentInStateAsync"/> (règles
/// SUP01b, configurable par état) et <see cref="GetDocumentsAsync"/> (compteurs par état du dashboard SUP02,
/// via <see cref="SetCountsByState"/>) sont utilisées ; les autres lectures lèvent (non sollicitées).
/// </summary>
internal sealed class FakeDocumentQueries : IDocumentQueries
{
    private readonly Dictionary<string, DocumentSummaryDto?> _oldestByState = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, int> _countsByState = new Dictionary<string, int>(StringComparer.Ordinal);

    public void SetOldestInState(string state, DocumentSummaryDto? oldest) => _oldestByState[state] = oldest;

    /// <summary>Compteurs par état renvoyés dans <c>CountsByState</c> par <see cref="GetDocumentsAsync"/> (dashboard SUP02).</summary>
    public void SetCountsByState(IReadOnlyDictionary<string, int> counts) => _countsByState = counts;

    public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) =>
        Task.FromResult(_oldestByState.TryGetValue(state, out var oldest) ? oldest : null);

    public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DocumentListResult
        {
            Items = [],
            Page = 1,
            PageSize = filter.PageSize,
            TotalCount = 0,
            CountsByState = _countsByState,
        });

    public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
