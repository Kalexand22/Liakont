namespace Liakont.Agent.Core.Storage;

/// <summary>Nature d'un élément de la file de push (F12 §2.2, §2.3).</summary>
public enum QueueItemKind
{
    /// <summary>Document pivot (payload JSON), clé d'idempotence = (source_reference, payload_hash).</summary>
    Document = 1,

    /// <summary>PDF lié à un document source (capacité ProvidesSourceDocuments).</summary>
    Pdf = 2,

    /// <summary>PDF d'un pool non lié (capacité ProvidesUnlinkedDocumentPool).</summary>
    PdfPool = 3,
}
