namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Empreinte d'un fichier membre d'un paquet d'archive : son nom (relatif au répertoire du paquet) et
/// son empreinte SHA-256 (hex minuscule). Brique du calcul de l'empreinte de paquet
/// (<see cref="PackageHasher"/>) et du manifest.
/// </summary>
/// <param name="Name">Nom du fichier, relatif au répertoire du paquet (ex. « payload.json »).</param>
/// <param name="Sha256">Empreinte SHA-256 du contenu, en hexadécimal minuscule.</param>
public sealed record ArchiveFileFingerprint(string Name, string Sha256);
