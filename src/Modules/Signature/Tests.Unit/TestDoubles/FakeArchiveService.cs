namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Archive.Contracts;

/// <summary>
/// Double de test d'<see cref="IArchiveService"/> : journalise les addenda rapatriés (preuve de signature)
/// pour prouver le rapatriement WORM via Contracts (jamais Archive.Domain — INV-YOUSIGN-6).
/// </summary>
internal sealed class FakeArchiveService : IArchiveService
{
    public List<ArchiveAddendumRequest> Addenda { get; } = [];

    public Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(
        ArchivePackageRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(request.DocumentId));

    public Task<ArchivePackageResult> AddAddendumAsync(
        ArchiveAddendumRequest request, CancellationToken cancellationToken = default)
    {
        Addenda.Add(request);
        return Task.FromResult(Result(request.DocumentId));
    }

    public Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Non utilisé par le drain.");

    private static ArchivePackageResult Result(Guid documentId) =>
        new(Guid.NewGuid(), documentId, "path", "hash", "chain", DateTimeOffset.UnixEpoch);
}
