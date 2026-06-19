namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;

/// <summary>
/// Réplique EN MÉMOIRE de <see cref="IReportingPieceLinkStore"/> : ajout idempotent (par tenant +
/// transmission + pièce) et lecture dans les deux sens, filtrée sur <c>companyId</c>, pour exercer
/// l'intégration de l'export contrôle fiscal sans base. La défense en profondeur company_id sur PostgreSQL
/// réel est prouvée par les tests d'intégration.
/// </summary>
public sealed class FakeReportingPieceLinkStore : IReportingPieceLinkStore
{
    private readonly List<ReportingPieceLink> _links = [];

    public Task<IReadOnlyList<ReportingPieceLink>> AppendAsync(
        Guid companyId,
        Guid documentId,
        IReadOnlyCollection<string> sourceReferences,
        CancellationToken cancellationToken = default)
    {
        foreach (string sourceReference in sourceReferences)
        {
            bool exists = _links.Any(l => l.CompanyId == companyId && l.DocumentId == documentId && l.SourceReference == sourceReference);
            if (!exists)
            {
                _links.Add(new ReportingPieceLink(Guid.NewGuid(), companyId, documentId, sourceReference, DateTimeOffset.UnixEpoch));
            }
        }

        return GetByDocumentAsync(companyId, documentId, cancellationToken);
    }

    public Task<IReadOnlyList<ReportingPieceLink>> GetByDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<ReportingPieceLink>)_links.Where(l => l.CompanyId == companyId && l.DocumentId == documentId).ToList());

    public Task<IReadOnlyList<ReportingPieceLink>> GetBySourceReferenceAsync(Guid companyId, string sourceReference, CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<ReportingPieceLink>)_links.Where(l => l.CompanyId == companyId && l.SourceReference == sourceReference).ToList());
}
