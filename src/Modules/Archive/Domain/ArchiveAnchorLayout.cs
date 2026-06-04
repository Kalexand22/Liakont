namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Convention d'arborescence des preuves d'ancrage temporel dans le coffre (TRK06). Les preuves sont au
/// niveau du TENANT (et non d'un document) : elles vivent sous <c>_anchors/</c>, à côté des répertoires
/// annuels des paquets. Le nom est dérivé du préfixe de l'empreinte de tête de chaîne et du préfixe de
/// l'empreinte de la preuve — déterministe et anti-collision, comme les addenda (TRK05). Tous les segments
/// sont assainis par <see cref="ArchivePackageLayout.SanitizeSegment"/> (anti path-traversal).
/// </summary>
public static class ArchiveAnchorLayout
{
    /// <summary>Racine (relative au tenant) des preuves d'ancrage.</summary>
    public const string AnchorsRoot = "_anchors";

    /// <summary>Chemin (relatif au tenant) de la preuve d'ancrage (jeton RFC 3161, fichier .ots).</summary>
    public static string ProofPath(string chainHeadPrefix, string proofHashPrefix, string extension) =>
        $"{AnchorsRoot}/{ArchivePackageLayout.SanitizeSegment(chainHeadPrefix)}/" +
        ArchivePackageLayout.SanitizeSegment($"anchor-{proofHashPrefix}.{extension}");

    /// <summary>Chemin (relatif au tenant) du manifest JSON décrivant la preuve d'ancrage.</summary>
    public static string ProofManifestPath(string chainHeadPrefix, string proofHashPrefix) =>
        $"{AnchorsRoot}/{ArchivePackageLayout.SanitizeSegment(chainHeadPrefix)}/" +
        ArchivePackageLayout.SanitizeSegment($"anchor-{proofHashPrefix}.json");
}
