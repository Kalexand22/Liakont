namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// Pièce binaire jointe à un paquet d'archive (facture légale générée par la PA, bordereau source,
/// tax-report en addendum…). Le contenu est conservé EXACT, tel que produit/reçu — le coffre n'altère
/// jamais une pièce.
/// </summary>
/// <param name="FileName">Nom du fichier dans le paquet (assaini par le module), ex. « facture-pa.pdf ».</param>
/// <param name="ContentType">Type MIME (ex. « application/pdf », « application/xml »).</param>
/// <param name="Content">Contenu binaire exact de la pièce.</param>
public sealed record ArchiveAttachment(string FileName, string ContentType, byte[] Content);
