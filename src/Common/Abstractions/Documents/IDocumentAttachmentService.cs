namespace Stratum.Common.Abstractions.Documents;

public interface IDocumentAttachmentService
{
    long MaxFileSizeBytes { get; }

    Task<IReadOnlyList<AttachedDocumentInfo>> ListByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default);

    Task<Guid> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        string entityType,
        string entityId,
        CancellationToken ct = default);

    Task DeleteAsync(Guid documentId, CancellationToken ct = default);

    string GetDownloadUrl(Guid documentId);
}
