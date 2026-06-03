namespace Stratum.Common.Abstractions.BlobStorage;

/// <summary>
/// Immutable reference to a blob stored via <see cref="IBlobStore"/>.
/// Used in cross-tenant events to reference attached documents without embedding content.
/// </summary>
public sealed record BlobReference(
    string StorageKey,
    string Filename,
    string ContentType,
    long SizeBytes);
