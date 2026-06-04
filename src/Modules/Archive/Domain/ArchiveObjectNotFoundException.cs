namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Levée par un <see cref="IArchiveStore"/> lorsqu'un objet attendu est introuvable à la lecture. Pour la
/// vérification d'intégrité, une pièce manquante est une anomalie détectée (et non une erreur technique
/// opaque) : la chaîne est rompue.
/// </summary>
public sealed class ArchiveObjectNotFoundException : Exception
{
    public ArchiveObjectNotFoundException()
    {
    }

    public ArchiveObjectNotFoundException(string message)
        : base(message)
    {
    }

    public ArchiveObjectNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception pour un chemin relatif (dans le tenant) introuvable.</summary>
    public static ArchiveObjectNotFoundException ForPath(string relativePath) =>
        new($"Pièce d'archive introuvable dans le coffre : « {relativePath} ».");
}
