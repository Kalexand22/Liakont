namespace Liakont.Modules.Archive.Domain;

using System.Globalization;

/// <summary>
/// Convention d'arborescence d'un paquet d'archive GÉNÉRIQUE (GED, F19 §5.1) : sous la racine du tenant
/// (ajoutée par le <see cref="IArchiveStore"/>), un document GED-seul vit dans
/// <c>_ged/{kind}/{année}/{mois}/{clé}/</c> — un espace d'octets WORM SÉPARÉ de la chaîne fiscale
/// (<c>{année}/{mois}/…</c>, <see cref="ArchivePackageLayout"/>). Le préfixe <c>_ged/</c> garantit qu'un
/// paquet GED est STRUCTURELLEMENT absent d'un export de contrôle fiscal (qui n'énumère que la chaîne fiscale,
/// dont les chemins commencent par <c>{année}/{mois}/…</c>). Les segments dynamiques (kind, clé) sont ASSAINIS
/// via <see cref="ArchivePackageLayout.SanitizeSegment"/> (anti path-traversal, comme le paquet fiscal) ; le
/// préfixe est un littéral FIXE, jamais fourni par l'appelant.
/// </summary>
public static class GedArchivePackageLayout
{
    /// <summary>Racine des paquets GED — un espace WORM séparé de la chaîne fiscale (F19 §5.1).</summary>
    public const string GedRootSegment = "_ged";

    /// <summary>Le manifest d'un paquet GED (empreintes des pièces + axes d'index).</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>Le rendu lisible autonome OPTIONNEL d'un paquet GED (aperçu console).</summary>
    public const string ReadableHtmlFileName = "document-lisible.html";

    /// <summary>Construit le répertoire (chemin relatif au tenant) d'un paquet GED, terminé par « / ».</summary>
    public static string PackageDirectory(string archiveKind, int filedYear, int filedMonth, string archiveKey)
    {
        if (filedMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(filedMonth), filedMonth, "Le mois de rangement doit être compris entre 1 et 12.");
        }

        string kind = ArchivePackageLayout.SanitizeSegment(archiveKind);
        string key = ArchivePackageLayout.SanitizeSegment(archiveKey);
        string year = filedYear.ToString("D4", CultureInfo.InvariantCulture);
        string month = filedMonth.ToString("D2", CultureInfo.InvariantCulture);
        return $"{GedRootSegment}/{kind}/{year}/{month}/{key}/";
    }
}
