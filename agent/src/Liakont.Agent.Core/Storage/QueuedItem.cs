namespace Liakont.Agent.Core.Storage;

using System;

/// <summary>Élément tel que stocké dans <c>push_queue</c> (sortie de <see cref="LocalQueue.PeekPending"/>).</summary>
public sealed class QueuedItem
{
    public QueuedItem(
        long id,
        QueueItemKind kind,
        string sourceReference,
        string? payloadHash,
        string? payloadJson,
        string? filePath,
        QueueItemStatus status,
        int attempts,
        string? lastError,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        Id = id;
        Kind = kind;
        SourceReference = sourceReference;
        PayloadHash = payloadHash;
        PayloadJson = payloadJson;
        FilePath = filePath;
        Status = status;
        Attempts = attempts;
        LastError = lastError;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public long Id { get; }

    public QueueItemKind Kind { get; }

    public string SourceReference { get; }

    public string? PayloadHash { get; }

    public string? PayloadJson { get; }

    public string? FilePath { get; }

    public QueueItemStatus Status { get; }

    public int Attempts { get; }

    public string? LastError { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; }
}
