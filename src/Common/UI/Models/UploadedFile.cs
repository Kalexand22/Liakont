namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents a file uploaded via StratumFileUpload.
/// The Stream must be consumed before the component's OnUpload callback completes.
/// </summary>
public sealed record UploadedFile(
    string Name,
    long Size,
    string ContentType,
    Stream Stream);
