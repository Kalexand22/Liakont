namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Stores.S3;

/// <summary>
/// Double en mémoire de <see cref="IS3BlobClient"/> : permet de tester <c>S3ArchiveStore</c> (mapping des
/// clés, WORM applicatif, pilotage de l'Object Lock par la capacité) sans backend réel. Mémorise si
/// l'Object Lock a été demandé par clé, pour vérifier qu'il suit bien la capacité déclarée.
/// </summary>
public sealed class FakeS3BlobClient : IS3BlobClient
{
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public Dictionary<string, bool> ObjectLockApplied { get; } = new(StringComparer.Ordinal);

    public Task<bool> TryPutIfAbsentAsync(string key, byte[] content, bool applyObjectLock, CancellationToken cancellationToken)
    {
        // Création atomique : si la clé existe déjà, on REFUSE (write-once), sans écraser le contenu.
        if (_objects.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        _objects[key] = content.ToArray();
        ObjectLockApplied[key] = applyObjectLock;
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.ContainsKey(key));

    public Task<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.TryGetValue(key, out byte[]? content) ? content.ToArray() : null);
}
