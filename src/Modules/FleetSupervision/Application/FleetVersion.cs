namespace Liakont.Modules.FleetSupervision.Application;

using System;

/// <summary>
/// Comparaison de versions de plateforme pour l'alerte « version obsolète » (OPS04). La version rapportée
/// par une instance vient de l'<c>AssemblyInformationalVersion</c> (forme <c>major.minor.patch+hash</c>) ;
/// on n'en garde que le cœur sémantique. Politique CONSERVATRICE : si l'une des deux versions est illisible,
/// on NE déclenche PAS l'alerte (pas de faux positif sur une version non parsable).
/// </summary>
public static class FleetVersion
{
    /// <summary>
    /// Extrait le cœur <see cref="Version"/> (major.minor[.build[.revision]]) d'une chaîne de version, en
    /// ignorant le suffixe de métadonnées (<c>+hash</c>) ou de pré-version (<c>-rc1</c>). Renvoie
    /// <c>null</c> si la chaîne est vide ou non parsable.
    /// </summary>
    public static Version? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string core = raw.Trim();
        int cut = core.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0)
        {
            core = core[..cut];
        }

        return Version.TryParse(core, out Version? version) ? version : null;
    }

    /// <summary>
    /// Vrai si <paramref name="instanceVersion"/> est STRICTEMENT antérieure à
    /// <paramref name="latestVersion"/>. Faux si l'une des deux est illisible (pas de faux positif).
    /// </summary>
    public static bool IsObsolete(string? instanceVersion, string? latestVersion)
    {
        Version? instance = Parse(instanceVersion);
        Version? latest = Parse(latestVersion);
        if (instance is null || latest is null)
        {
            return false;
        }

        return instance < latest;
    }
}
