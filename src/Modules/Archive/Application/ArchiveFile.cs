namespace Liakont.Modules.Archive.Application;

/// <summary>Un fichier membre d'un paquet d'archive, avec son empreinte SHA-256 (hex minuscule) déjà calculée.</summary>
/// <param name="Name">Nom du fichier (relatif au répertoire du paquet).</param>
/// <param name="ContentType">Type MIME.</param>
/// <param name="Content">Contenu binaire exact.</param>
/// <param name="Sha256">Empreinte SHA-256 du contenu (hex minuscule).</param>
public sealed record ArchiveFile(string Name, string ContentType, byte[] Content, string Sha256);
