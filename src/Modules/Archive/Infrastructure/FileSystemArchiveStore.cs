namespace Liakont.Modules.Archive.Infrastructure;

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Options;

/// <summary>
/// Coffre sur système de fichiers (store V1 par défaut, appliance self-hosted — blueprint §6 ; ADR-0009).
/// Un répertoire par tenant sous la racine d'instance. WORM applicatif : écriture write-once (idempotente
/// pour un contenu identique, conflit sinon) et fichier passé en lecture seule après écriture. Aucune
/// capacité native (<see cref="ArchiveStoreCapabilities.None"/>) : l'intégrité repose ENTIÈREMENT sur la
/// chaîne de hashes produit (blueprint §6). Aucune méthode de suppression/modification n'est exposée.
/// </summary>
public sealed class FileSystemArchiveStore : IArchiveStore
{
    private readonly string _rootPath;

    public FileSystemArchiveStore(IOptions<FileSystemArchiveStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string root = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Le coffre FileSystem requiert une racine (Archive:Storage:FileSystem:RootPath).");
        }

        _rootPath = Path.GetFullPath(root);
    }

    public ArchiveStoreCapabilities Capabilities => ArchiveStoreCapabilities.None;

    public async Task WriteAsync(string tenant, string relativePath, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string fullPath = ResolveFullPath(tenant, relativePath);

        if (File.Exists(fullPath))
        {
            await EnsureIdenticalOrConflictAsync(fullPath, relativePath, content, cancellationToken);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content, cancellationToken);
            }

            try
            {
                File.Move(tempPath, fullPath); // atomique sur le même volume ; jamais de fichier partiel au chemin canonique
            }
            catch (IOException) when (File.Exists(fullPath))
            {
                // Course perdue : une autre écriture a posé le fichier — idempotent si identique, conflit sinon.
                await EnsureIdenticalOrConflictAsync(fullPath, relativePath, content, cancellationToken);
                return; // le finally nettoie le temp
            }

            File.SetAttributes(fullPath, FileAttributes.ReadOnly);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public Task<bool> ExistsAsync(string tenant, string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolveFullPath(tenant, relativePath)));
    }

    public async Task<byte[]> ReadAsync(string tenant, string relativePath, CancellationToken cancellationToken = default)
    {
        string fullPath = ResolveFullPath(tenant, relativePath);
        if (!File.Exists(fullPath))
        {
            throw ArchiveObjectNotFoundException.ForPath(relativePath);
        }

        return await File.ReadAllBytesAsync(fullPath, cancellationToken);
    }

    private static async Task EnsureIdenticalOrConflictAsync(string fullPath, string relativePath, byte[] content, CancellationToken cancellationToken)
    {
        byte[] existing = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (!existing.AsSpan().SequenceEqual(content))
        {
            throw ArchiveWriteConflictException.ForPath(relativePath);
        }
    }

    private string ResolveFullPath(string tenant, string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        string tenantSegment = ArchivePackageLayout.SanitizeSegment(tenant);
        string tenantRoot = Path.GetFullPath(Path.Combine(_rootPath, tenantSegment));

        // Recompose le chemin segment par segment, chaque segment ré-assaini (défense anti path-traversal).
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException($"Chemin d'archive vide : « {relativePath} ».", nameof(relativePath));
        }

        string combined = tenantRoot;
        foreach (string segment in segments)
        {
            combined = Path.Combine(combined, ArchivePackageLayout.SanitizeSegment(segment));
        }

        string fullPath = Path.GetFullPath(combined);
        if (!fullPath.StartsWith(tenantRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(fullPath, tenantRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Chemin d'archive hors du périmètre du tenant : « {relativePath} ».", nameof(relativePath));
        }

        return fullPath;
    }
}
