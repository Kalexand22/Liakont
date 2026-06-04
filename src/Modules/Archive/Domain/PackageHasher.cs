namespace Liakont.Modules.Archive.Domain;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Empreinte d'un PAQUET (ou d'un addendum) du coffre, calculée à partir des empreintes de ses fichiers
/// membres. Déterministe et indépendante de l'ordre d'énumération : les empreintes sont triées par nom
/// de fichier (ordinal), concaténées sous la forme <c>name:hash\n</c>, puis hachées en SHA-256.
///
/// L'empreinte de paquet couvre les fichiers de CONTENU ; le <c>manifest.json</c> (qui contient lui-même
/// l'empreinte de paquet et le <c>chain_hash</c>) en est EXCLU pour éviter toute circularité. La
/// vérification recalcule cette empreinte depuis le contenu réel du coffre et la compare à la valeur
/// scellée en base (TRK05 §6) — toute altération d'un fichier la fait diverger.
/// </summary>
public static class PackageHasher
{
    /// <summary>Calcule l'empreinte de paquet (hex minuscule) à partir des empreintes de ses fichiers de contenu.</summary>
    public static string Compute(IReadOnlyCollection<ArchiveFileFingerprint> fileFingerprints)
    {
        ArgumentNullException.ThrowIfNull(fileFingerprints);
        if (fileFingerprints.Count == 0)
        {
            throw new ArgumentException("Un paquet d'archive doit contenir au moins un fichier de contenu.", nameof(fileFingerprints));
        }

        var canonical = new StringBuilder();
        foreach (ArchiveFileFingerprint fingerprint in fileFingerprints
            .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            canonical.Append(fingerprint.Name).Append(':').Append(fingerprint.Sha256).Append('\n');
        }

        return Sha256Hex.OfString(canonical.ToString());
    }
}
