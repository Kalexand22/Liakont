namespace Liakont.Modules.Staging.Infrastructure;

using System;
using System.Text;

/// <summary>
/// Disposition des chemins du magasin de staging FileSystem : un fichier chiffré par document, sous un
/// répertoire par tenant. Assainit chaque segment (défense anti path-traversal) sur le même principe que le
/// coffre d'archive, sans coupler le module Staging au module Archive (frontière Contracts uniquement).
/// </summary>
internal static class StagingPathLayout
{
    /// <summary>Extension du blob de payload chiffré (au repos).</summary>
    public const string PayloadFileExtension = ".payload.enc";

    /// <summary>
    /// Assainit un segment de chemin : réduit au nom de base (anti path-traversal), puis REMPLACE tout
    /// caractère hors <c>[A-Za-z0-9-_.]</c> par <c>_</c> (jamais supprimé — la suppression rendrait le
    /// mapping non injectif et casserait l'isolation tenant). Aligné sur
    /// <c>ArchivePackageLayout.SanitizeSegment</c>. Lève si le résultat est vide, <c>.</c> ou <c>..</c>.
    /// </summary>
    /// <param name="segment">Le segment brut (ex. identifiant de tenant).</param>
    /// <returns>Le segment assaini, sûr à composer dans un chemin.</returns>
    public static string SanitizeSegment(string segment)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);

        // Anti path-traversal : on ne garde que le nom de base, jamais un séparateur fourni en entrée.
        string baseName = segment;
        int lastSeparator = baseName.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            baseName = baseName[(lastSeparator + 1)..];
        }

        var builder = new StringBuilder(baseName.Length);
        foreach (char c in baseName)
        {
            bool allowed = (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.';
            builder.Append(allowed ? c : '_');
        }

        string result = builder.ToString();
        if (result.Length == 0 || string.Equals(result, ".", StringComparison.Ordinal) || string.Equals(result, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Segment de chemin de staging invalide : « {segment} ».", nameof(segment));
        }

        return result;
    }
}
