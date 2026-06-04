namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;

/// <summary>
/// Coffre en mémoire pour les tests : applique la même sémantique WORM que les stores réels (write-once
/// idempotent, conflit si contenu différent). <see cref="Tamper"/> simule une altération du backend de
/// stockage (un attaquant qui réécrit un fichier en contournant le produit) pour les tests de détection.
/// </summary>
public sealed class InMemoryArchiveStore : IArchiveStore
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public ArchiveStoreCapabilities Capabilities => ArchiveStoreCapabilities.None;

    public int ObjectCount => _objects.Count;

    public Task WriteAsync(string tenant, string relativePath, byte[] content, CancellationToken cancellationToken = default)
    {
        string key = Key(tenant, relativePath);
        if (_objects.TryGetValue(key, out byte[]? existing))
        {
            if (!existing.AsSpan().SequenceEqual(content))
            {
                throw ArchiveWriteConflictException.ForPath(relativePath);
            }

            return Task.CompletedTask;
        }

        _objects[key] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string tenant, string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objects.ContainsKey(Key(tenant, relativePath)));

    public Task<byte[]> ReadAsync(string tenant, string relativePath, CancellationToken cancellationToken = default)
    {
        if (_objects.TryGetValue(Key(tenant, relativePath), out byte[]? content))
        {
            return Task.FromResult(content.ToArray());
        }

        throw ArchiveObjectNotFoundException.ForPath(relativePath);
    }

    /// <summary>Réécrit (test only) un objet pour simuler une altération du backend, hors discipline WORM produit.</summary>
    public void Tamper(string tenant, string relativePath, byte[] content) => _objects[Key(tenant, relativePath)] = content.ToArray();

    /// <summary>Supprime (test only) un objet pour simuler une pièce manquante.</summary>
    public void Remove(string tenant, string relativePath) => _objects.TryRemove(Key(tenant, relativePath), out _);

    private static string Key(string tenant, string relativePath) => tenant + "::" + relativePath;
}
