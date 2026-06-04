namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;

/// <summary>Service d'archive fictif : enregistre les addenda ajoutés (preuve WORM) pour assertion.</summary>
internal sealed class RecordingArchiveService : IArchiveService
{
    public List<ArchiveAddendumRequest> Addenda { get; } = [];

    public Task<ArchivePackageResult> AddAddendumAsync(ArchiveAddendumRequest request, CancellationToken cancellationToken = default)
    {
        Addenda.Add(request);
        return Task.FromResult(new ArchivePackageResult(
            Guid.NewGuid(), request.DocumentId, "package/path", "package-hash", "chain-hash", new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero)));
    }

    public Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(ArchivePackageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
