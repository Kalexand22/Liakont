namespace Liakont.Agent.Core.Extraction;

using System;

/// <summary>
/// Pièce jointe (PDF) liée à un document source (capacité <see cref="ExtractorCapabilities.ProvidesSourceDocuments"/>).
/// Pointe un fichier sur disque — l'agent transporte le fichier, il n'en interprète pas le contenu.
/// </summary>
public sealed class SourceAttachment
{
    /// <summary>Crée une pièce jointe liée.</summary>
    /// <param name="sourceReference">Référence source du document auquel le PDF est lié.</param>
    /// <param name="filePath">Chemin du fichier PDF sur disque.</param>
    /// <param name="fileName">Nom de fichier suggéré (par défaut, le nom du fichier pointé).</param>
    public SourceAttachment(string sourceReference, string filePath, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            throw new ArgumentException("La référence source de la pièce jointe est requise.", nameof(sourceReference));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Le chemin du fichier de la pièce jointe est requis.", nameof(filePath));
        }

        SourceReference = sourceReference;
        FilePath = filePath;
        FileName = string.IsNullOrWhiteSpace(fileName) ? System.IO.Path.GetFileName(filePath) : fileName!;
    }

    /// <summary>Référence source du document auquel le PDF est lié.</summary>
    public string SourceReference { get; }

    /// <summary>Chemin du fichier PDF sur disque.</summary>
    public string FilePath { get; }

    /// <summary>Nom de fichier suggéré.</summary>
    public string FileName { get; }
}
