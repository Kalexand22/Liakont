namespace Liakont.Modules.SupportTrace.Infrastructure;

using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Disposition des chemins du store de trace de support : <c>&lt;racine&gt;/&lt;tenant&gt;/&lt;jour&gt;/&lt;document&gt;.fxtrace</c>.
/// La PARTITION PAR JOUR (date de transmission, UTC, format invariant <c>yyyy-MM-dd</c>) porte la RÉTENTION :
/// la purge supprime les répertoires-jour dont la date est antérieure à la borne — robuste (la date est dans
/// le chemin, pas dans un mtime). Chaque segment est assaini (défense anti path-traversal), sans coupler le
/// module à un autre (frontière Contracts uniquement).
/// </summary>
internal static class SupportTracePathLayout
{
    /// <summary>Extension du blob de trace de support chiffré (au repos).</summary>
    public const string TraceFileExtension = ".fxtrace";

    /// <summary>Format invariant du répertoire-jour (date de transmission, UTC).</summary>
    public const string DayDirectoryFormat = "yyyy-MM-dd";

    /// <summary>Le répertoire-jour (UTC) d'un horodatage de transmission.</summary>
    public static string DayDirectory(DateTimeOffset recordedAtUtc) =>
        recordedAtUtc.UtcDateTime.ToString(DayDirectoryFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Tente de parser un nom de répertoire-jour (<c>yyyy-MM-dd</c>, UTC). Renvoie <c>false</c> pour tout
    /// répertoire au nom non conforme (jamais supprimé par la purge : on n'efface que ce qu'on a écrit).
    /// </summary>
    public static bool TryParseDayDirectory(string name, out DateOnly day) =>
        DateOnly.TryParseExact(name, DayDirectoryFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>
    /// Assainit un segment de chemin : réduit au nom de base (anti path-traversal), puis REMPLACE tout
    /// caractère hors <c>[A-Za-z0-9-_.]</c> par <c>_</c> (jamais supprimé — la suppression rendrait le mapping
    /// non injectif et casserait l'isolation tenant). Même principe que <c>StagingPathLayout.SanitizeSegment</c>
    /// (aucun couplage : recopié pour rester dans la frontière du module). Lève si le résultat est vide,
    /// <c>.</c> ou <c>..</c>.
    /// </summary>
    public static string SanitizeSegment(string segment)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);

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
            throw new ArgumentException($"Segment de chemin de trace de support invalide : « {segment} ».", nameof(segment));
        }

        return result;
    }
}
