namespace Stratum.Common.Abstractions.Documents;

public sealed record AttachedDocumentInfo(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);
