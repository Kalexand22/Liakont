namespace Stratum.Common.UI.Models;

/// <summary>
/// Reports upload progress for a single file in StratumFileUpload.
/// </summary>
public sealed record UploadProgress(
    string FileName,
    int Percentage,
    long BytesUploaded,
    long TotalBytes);
