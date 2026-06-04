namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts;

/// <summary>Pool PDF en mémoire : liste configurée + contenu par identifiant.</summary>
internal sealed class FakePooledPdfStore : IIngestedPdfStore
{
    private readonly List<PooledPdfReference> _pool = [];
    private readonly Dictionary<string, byte[]> _content = [];

    public void Add(string poolPdfId, string fileName, byte[] content)
    {
        _pool.Add(new PooledPdfReference(poolPdfId, fileName));
        _content[poolPdfId] = content;
    }

    public Task<IReadOnlyList<PooledPdfReference>> ListPooledPdfsAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PooledPdfReference>>(_pool);

    public Task<Stream> OpenPooledPdfAsync(string tenantId, string poolPdfId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(_content[poolPdfId], writable: false));

    public Task<string> SaveLinkedPdfAsync(string tenantId, string sourceReference, Stream content, CancellationToken cancellationToken = default) =>
        throw new System.NotSupportedException();

    public Task<string> SavePooledPdfAsync(string tenantId, string fileName, Stream content, CancellationToken cancellationToken = default) =>
        throw new System.NotSupportedException();
}
