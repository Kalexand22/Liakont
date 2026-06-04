namespace Liakont.Modules.Archive.Domain;

using System.Globalization;
using System.Text;

/// <summary>
/// Convention d'arborescence d'un paquet d'archive (TRK05 §2) : sous la racine du tenant (ajoutée par le
/// <see cref="IArchiveStore"/>), un paquet vit dans <c>{année}/{mois}/{numéro-document}/</c> et porte des
/// fichiers de noms stables. Tous les segments de chemin sont ASSAINIS (anti path-traversal, comme
/// ADR-0008 pour le pool PDF) : seuls <c>[A-Za-z0-9-_.]</c> survivent, tout autre caractère devient
/// <c>_</c> ; aucun séparateur de chemin fourni par un appelant n'est honoré.
/// </summary>
public static class ArchivePackageLayout
{
    /// <summary>Le payload exact transmis à la PA (TRK05 §2).</summary>
    public const string PayloadFileName = "payload.json";

    /// <summary>La réponse brute de la PA + identifiants DGFiP (preuve de transmission).</summary>
    public const string PaResponseFileName = "reponse-pa.json";

    /// <summary>Le rendu lisible autonome (art. 289 V CGI), ouvrable sans le logiciel.</summary>
    public const string ReadableHtmlFileName = "document-lisible.html";

    /// <summary>Le manifest du paquet (empreintes, chaînage, pièces présentes/absentes).</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>Construit le répertoire (chemin relatif au tenant) d'un paquet, terminé par « / ».</summary>
    public static string PackageDirectory(int issueYear, int issueMonth, string documentNumber)
    {
        if (issueMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(issueMonth), issueMonth, "Le mois d'émission doit être compris entre 1 et 12.");
        }

        string number = SanitizeSegment(documentNumber);
        string year = issueYear.ToString("D4", CultureInfo.InvariantCulture);
        string month = issueMonth.ToString("D2", CultureInfo.InvariantCulture);
        return $"{year}/{month}/{number}/";
    }

    /// <summary>Construit le nom de fichier d'addendum chaîné (numéroté à partir de 1), assaini.</summary>
    public static string AddendumManifestFileName(int sequence)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Le numéro d'addendum commence à 1.");
        }

        return $"manifest-addendum-{sequence.ToString("D3", CultureInfo.InvariantCulture)}.json";
    }

    /// <summary>Combine un répertoire de paquet et un nom de fichier (assaini) en un chemin relatif au tenant.</summary>
    public static string Combine(string packageDirectory, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageDirectory);
        return packageDirectory + SanitizeSegment(fileName);
    }

    /// <summary>
    /// Assainit un segment de chemin (slug de tenant, numéro de document, nom de fichier) : réduit au nom
    /// de base, puis ne conserve que <c>[A-Za-z0-9-_.]</c>. Lève si le résultat est vide (un segment vide
    /// masquerait une donnée et casserait l'adressage du coffre).
    /// </summary>
    public static string SanitizeSegment(string segment)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);

        // Anti path-traversal : on ne garde que le nom de base, jamais un segment de chemin fourni en entrée.
        string baseName = segment;
        int lastSlash = baseName.LastIndexOfAny(['/', '\\']);
        if (lastSlash >= 0)
        {
            baseName = baseName[(lastSlash + 1)..];
        }

        var sanitized = new StringBuilder(baseName.Length);
        foreach (char c in baseName)
        {
            sanitized.Append(IsAllowed(c) ? c : '_');
        }

        string result = sanitized.ToString();
        if (result.Length == 0 || result == "." || result == "..")
        {
            throw new ArgumentException($"Segment de chemin invalide après assainissement : « {segment} ».", nameof(segment));
        }

        return result;
    }

    private static bool IsAllowed(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}
