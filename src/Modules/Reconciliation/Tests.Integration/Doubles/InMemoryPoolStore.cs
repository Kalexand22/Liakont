namespace Liakont.Modules.Reconciliation.Tests.Integration.Doubles;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts;

/// <summary>
/// Pool PDF en mémoire pour les tests d'intégration de flux : sert des PDF RÉELS (octets) à PdfPig et au
/// builder d'addendum. Le store FICHIER réel (<c>FileSystemIngestedPdfStore</c>) est testé séparément
/// dans <c>Ingestion.Tests.Integration</c> ; ici on isole le flux de réconciliation (moteur + addendum
/// d'archive réel + file d'attente réelle).
/// </summary>
internal sealed class InMemoryPoolStore : IIngestedPdfStore
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
