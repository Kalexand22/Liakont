namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;

/// <summary>Double de <see cref="IArchiveService"/> qui ENREGISTRE tous les addenda reçus (assertions WORM).</summary>
internal sealed class RecordingArchiveService : IArchiveService
{
    public List<ArchiveAddendumRequest> Addenda { get; } = new();

    public ArchiveAddendumRequest? LastAddendum { get; private set; }

    public Task<ArchivePackageResult> AddAddendumAsync(ArchiveAddendumRequest request, CancellationToken cancellationToken = default)
    {
        Addenda.Add(request);
        LastAddendum = request;
        return Task.FromResult(new ArchivePackageResult(
            Guid.NewGuid(), request.DocumentId, "tenant/2026/01/" + request.DocumentNumber, "package-hash-test", "chain-hash-test", DateTimeOffset.UnixEpoch));
    }

    public Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(ArchivePackageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
