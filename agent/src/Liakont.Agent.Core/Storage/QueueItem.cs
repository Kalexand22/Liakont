namespace Liakont.Agent.Core.Storage;

using System;

/// <summary>
/// Élément à enfiler dans la file de push (entrée de <see cref="LocalQueue.Enqueue"/>). Un document
/// porte son payload JSON canonique et son empreinte ; un PDF porte son chemin de fichier.
/// </summary>
public sealed class QueueItem
{
    private QueueItem(QueueItemKind kind, string sourceReference, string? payloadHash, string? payloadJson, string? filePath)
    {
        Kind = kind;
        SourceReference = sourceReference;
        PayloadHash = payloadHash;
        PayloadJson = payloadJson;
        FilePath = filePath;
    }

    public QueueItemKind Kind { get; }

    /// <summary>Référence source du document (clé fonctionnelle stable, jamais ré-inventée par l'agent).</summary>
    public string SourceReference { get; }

    /// <summary>Empreinte canonique du payload (anti-doublon) ; requise pour un document.</summary>
    public string? PayloadHash { get; }

    /// <summary>Payload JSON canonique du document pivot ; requis pour un document.</summary>
    public string? PayloadJson { get; }

    /// <summary>Chemin du fichier PDF ; requis pour un PDF (lié ou pool).</summary>
    public string? FilePath { get; }

    /// <summary>Crée un élément « document pivot » (payload + empreinte obligatoires).</summary>
    public static QueueItem ForDocument(string sourceReference, string payloadHash, string payloadJson)
    {
        RequireNonEmpty(sourceReference, nameof(sourceReference));
        RequireNonEmpty(payloadHash, nameof(payloadHash));
        RequireNonEmpty(payloadJson, nameof(payloadJson));
        return new QueueItem(QueueItemKind.Document, sourceReference, payloadHash, payloadJson, filePath: null);
    }

    /// <summary>Crée un élément « PDF » (lié ou pool) à partir d'un chemin de fichier.</summary>
    public static QueueItem ForPdf(QueueItemKind kind, string sourceReference, string filePath, string? payloadHash = null)
    {
        if (kind != QueueItemKind.Pdf && kind != QueueItemKind.PdfPool)
        {
            throw new ArgumentException("Un PDF doit être de type Pdf ou PdfPool.", nameof(kind));
        }

        RequireNonEmpty(sourceReference, nameof(sourceReference));
        RequireNonEmpty(filePath, nameof(filePath));
        return new QueueItem(kind, sourceReference, payloadHash, payloadJson: null, filePath: filePath);
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"La valeur « {paramName} » est requise.", paramName);
        }
    }
}
