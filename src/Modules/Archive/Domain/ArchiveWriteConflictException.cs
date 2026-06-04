namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Levée par un <see cref="IArchiveStore"/> lorsqu'un contenu DIFFÉRENT existe déjà au chemin demandé :
/// un paquet d'archive ne se réécrit jamais (WORM, CLAUDE.md n°4). Une ré-écriture du même contenu, elle,
/// est idempotente et ne lève pas (reprise sûre après incident).
/// </summary>
public sealed class ArchiveWriteConflictException : Exception
{
    public ArchiveWriteConflictException()
    {
    }

    public ArchiveWriteConflictException(string message)
        : base(message)
    {
    }

    public ArchiveWriteConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception pour un chemin relatif (dans le tenant) en conflit.</summary>
    public static ArchiveWriteConflictException ForPath(string relativePath) =>
        new($"Le coffre est write-once (WORM) : un contenu différent existe déjà à « {relativePath} » et ne peut être réécrit (CLAUDE.md n.4).");
}
