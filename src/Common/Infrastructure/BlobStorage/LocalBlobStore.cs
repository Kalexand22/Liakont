namespace Stratum.Common.Infrastructure.BlobStorage;

using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.BlobStorage;

/// <summary>
/// Filesystem-backed implementation of <see cref="IBlobStore"/>.
/// Stores blobs under {BasePath}/{containerHint}/{uuid}.{ext}.
/// Registered as Singleton — implementation is stateless after construction.
/// </summary>
public sealed class LocalBlobStore : IBlobStore
{
    private readonly string _basePath;

    public LocalBlobStore(IOptions<BlobStorageOptions> options)
    {
        var resolved = Path.GetFullPath(options.Value.BasePath);

        // Ensure trailing separator so StartsWith cannot match partial directory names
        _basePath = resolved.EndsWith(Path.DirectorySeparatorChar)
            ? resolved
            : resolved + Path.DirectorySeparatorChar;
    }

    public async Task<BlobReference> PutAsync(
        string containerHint,
        string filename,
        string contentType,
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerHint);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var sanitizedContainer = SanitizePath(containerHint);
        var id = Guid.NewGuid().ToString("N");
        var ext = Path.GetExtension(filename);
        var storageFilename = string.IsNullOrEmpty(ext) ? id : $"{id}{ext}";
        var storageKey = $"{sanitizedContainer}/{storageFilename}";

        var fullPath = ResolveSafePath(storageKey);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(
            fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);

        var sizeBytes = fileStream.Length;

        return new BlobReference(storageKey, filename, contentType, sizeBytes);
    }

    public Task<Stream> GetAsync(string storageKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var fullPath = ResolveSafePath(storageKey);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Blob not found for storage key '{storageKey}'.", fullPath);
        }

        Stream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var fullPath = ResolveSafePath(storageKey);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cosmetic sanitization of container hint. Strips path separators and dots.
    /// Real security is enforced by <see cref="ResolveSafePath"/>.
    /// </summary>
    private static string SanitizePath(string input)
    {
        // Replace any path separator or dot-dot sequence
        var sanitized = input
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace("..", "_");

        // Remove any remaining chars invalid for filenames
        var invalidChars = Path.GetInvalidFileNameChars();
        sanitized = string.Join("_", sanitized.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    /// <summary>
    /// Resolves a storage key to a full filesystem path, ensuring the result
    /// stays under <see cref="_basePath"/>. Prevents path traversal attacks.
    /// </summary>
    private string ResolveSafePath(string storageKey)
    {
        // Normalize backslashes to forward slashes so that path traversal
        // via '..\\' is detected on Linux where '\' is not a separator.
        var normalized = storageKey.Replace('\\', '/');
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, normalized));

        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Storage key '{storageKey}' resolves outside the blob storage root.");
        }

        return fullPath;
    }
}
