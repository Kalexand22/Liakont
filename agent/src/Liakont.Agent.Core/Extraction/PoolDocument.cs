namespace Liakont.Agent.Core.Extraction;

using System;

/// <summary>
/// PDF d'un pool NON lié (capacité <see cref="ExtractorCapabilities.ProvidesUnlinkedDocumentPool"/>) :
/// un fichier déposé en vrac, sans lien fiable vers un document. La réconciliation fichier ↔ document
/// vit sur la PLATEFORME (F06/TRK07) ; l'agent ne fait que transporter le fichier et sa clé stable.
/// </summary>
public sealed class PoolDocument
{
    /// <summary>Crée un document de pool.</summary>
    /// <param name="poolReference">Clé STABLE du fichier dans le pool (sert de clé d'idempotence de file ; par défaut le nom du fichier).</param>
    /// <param name="filePath">Chemin du fichier PDF sur disque.</param>
    /// <param name="fileName">Nom de fichier suggéré (par défaut, le nom du fichier pointé).</param>
    public PoolDocument(string poolReference, string filePath, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(poolReference))
        {
            throw new ArgumentException("La référence du document de pool est requise.", nameof(poolReference));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Le chemin du fichier du document de pool est requis.", nameof(filePath));
        }

        PoolReference = poolReference;
        FilePath = filePath;
        FileName = string.IsNullOrWhiteSpace(fileName) ? System.IO.Path.GetFileName(filePath) : fileName!;
    }

    /// <summary>Clé stable du fichier dans le pool.</summary>
    public string PoolReference { get; }

    /// <summary>Chemin du fichier PDF sur disque.</summary>
    public string FilePath { get; }

    /// <summary>Nom de fichier suggéré.</summary>
    public string FileName { get; }
}
