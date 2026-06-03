namespace Stratum.Common.Infrastructure.BlobStorage;

/// <summary>
/// Configuration for <see cref="LocalBlobStore"/>.
/// Bound to the "BlobStorage" configuration section.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Root directory for local blob storage.
    /// Blobs are stored under {BasePath}/{containerHint}/{uuid}.{ext}.
    /// </summary>
    public string BasePath { get; set; } = "data/blobs";
}
