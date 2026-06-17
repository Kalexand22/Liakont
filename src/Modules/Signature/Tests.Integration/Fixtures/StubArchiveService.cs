namespace Liakont.Modules.Signature.Tests.Integration.Fixtures;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;

/// <summary>Stub de <see cref="IArchiveService"/> pour les tests d'intégration : renvoie une référence de paquet factice.</summary>
internal sealed class StubArchiveService : IArchiveService
{
    public Task<ArchivePackageResult> AddAddendumAsync(ArchiveAddendumRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ArchivePackageResult(
            Guid.NewGuid(), request.DocumentId, "tenant/2026/01/" + request.DocumentNumber, "package-hash-it", "chain-hash-it", DateTimeOffset.UnixEpoch));

    public Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(ArchivePackageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
