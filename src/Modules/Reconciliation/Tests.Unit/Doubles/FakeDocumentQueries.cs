namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;

/// <summary>Requêtes Documents fictives : documents émis configurés (par état + par id).</summary>
internal sealed class FakeDocumentQueries : IDocumentQueries
{
    private readonly List<DocumentSummaryDto> _issued = [];
    private readonly Dictionary<Guid, DocumentDto> _byId = [];

    public void AddIssued(Guid id, string number, DateOnly issueDate, decimal totalGross)
    {
        _issued.Add(new DocumentSummaryDto
        {
            Id = id,
            DocumentNumber = number,
            DocumentType = "Invoice",
            IssueDate = issueDate,
            TotalGross = totalGross,
            State = "Issued",
            LastUpdateUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        });

        _byId[id] = new DocumentDto
        {
            Id = id,
            SourceReference = "src-" + number,
            DocumentNumber = number,
            DocumentType = "Invoice",
            IssueDate = issueDate,
            CustomerIsCompanyHint = false,
            TotalNet = totalGross,
            TotalTax = 0m,
            TotalGross = totalGross,
            State = "Issued",
            PayloadHash = "hash-" + number,
            FirstSeenUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            LastUpdateUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        };
    }

    public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Une seule page suffit (< pageSize) : le service arrête sa pagination.
        IReadOnlyList<DocumentSummaryDto> result = page == 1 && state == "Issued" ? _issued : [];
        return Task.FromResult(result);
    }

    public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.FirstOrDefault(d => d.DocumentNumber == documentNumber));

    public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
