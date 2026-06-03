namespace Stratum.Common.Abstractions.BlobStorage;

/// <summary>
/// Abstraction for blob (binary large object) storage.
/// Phase 1 uses local filesystem; Phase 2 can swap to S3/Azure without changing consumers.
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Stores a blob and returns a reference with the generated storage key.
    /// </summary>
    /// <param name="containerHint">Logical grouping hint (e.g. "cross-tenant", "portal-uploads").</param>
    /// <param name="filename">Original filename for the blob.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="content">Readable stream with the blob content. The caller owns the stream lifetime.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="BlobReference"/> with the storage key and metadata.</returns>
    Task<BlobReference> PutAsync(
        string containerHint,
        string filename,
        string contentType,
        Stream content,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a readable stream for the blob identified by <paramref name="storageKey"/>.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    Task<Stream> GetAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes the blob identified by <paramref name="storageKey"/>.
    /// Does not throw if the blob does not exist.
    /// </summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
