namespace Liakont.Modules.Archive.Stores.S3;

using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Options;

/// <summary>
/// Coffre sur backend S3-COMPATIBLE (store V1 optionnel — blueprint §6 ; ADR-0009). Implémente
/// <see cref="IArchiveStore"/> au-dessus de la couture <see cref="IS3BlobClient"/> ; le module Archive ne
/// voit JAMAIS ce type concret (pas de <c>if (store is S3)</c>) — il est branché par configuration
/// d'instance. WORM applicatif : write-once idempotent (conflit si contenu différent) ; quand le backend
/// déclare l'Object Lock, celui-ci est appliqué EN PLUS de la chaîne de hashes produit (ceinture +
/// bretelles), jamais à sa place.
/// </summary>
public sealed class S3ArchiveStore : IArchiveStore
{
    private readonly IS3BlobClient _client;
    private readonly ArchiveStoreCapabilities _capabilities;

    public S3ArchiveStore(IS3BlobClient client, IOptions<S3ArchiveStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _capabilities = new ArchiveStoreCapabilities(
            options.Value.SupportsObjectLock,
            options.Value.SupportsLegalHold);
    }

    public ArchiveStoreCapabilities Capabilities => _capabilities;

    public async Task WriteAsync(string tenant, string relativePath, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string key = ResolveKey(tenant, relativePath);

        byte[]? existing = await _client.TryGetAsync(key, cancellationToken);
        if (existing is not null)
        {
            if (!existing.AsSpan().SequenceEqual(content))
            {
                throw ArchiveWriteConflictException.ForPath(relativePath);
            }

            return;
        }

        // applyObjectLock est piloté par la CAPACITÉ déclarée du store, pas par un test de type concret.
        await _client.PutAsync(key, content, _capabilities.SupportsObjectLock, cancellationToken);
    }

    public Task<bool> ExistsAsync(string tenant, string relativePath, CancellationToken cancellationToken = default) =>
        _client.ExistsAsync(ResolveKey(tenant, relativePath), cancellationToken);

    public async Task<byte[]> ReadAsync(string tenant, string relativePath, CancellationToken cancellationToken = default)
    {
        string key = ResolveKey(tenant, relativePath);
        byte[]? content = await _client.TryGetAsync(key, cancellationToken);
        return content ?? throw ArchiveObjectNotFoundException.ForPath(relativePath);
    }

    private static string ResolveKey(string tenant, string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException($"Chemin d'archive vide : « {relativePath} ».", nameof(relativePath));
        }

        var key = new StringBuilder(ArchivePackageLayout.SanitizeSegment(tenant));
        foreach (string segment in segments)
        {
            key.Append('/').Append(ArchivePackageLayout.SanitizeSegment(segment));
        }

        return key.ToString();
    }
}
