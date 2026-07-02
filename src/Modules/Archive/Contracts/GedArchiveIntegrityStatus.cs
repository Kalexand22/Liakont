namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// État d'intégrité d'un paquet GED rangé write-once (F19 §3.4.1/§6.7, option C). L'ancre d'intégrité de
/// RÉFÉRENCE est les OCTETS write-once de <see cref="Domain.IArchiveStore"/> ; le <c>content_hash</c> indexé
/// n'en est qu'une COPIE (INV-ARCH-GED-2). La vérification RE-LIT les octets du coffre et recalcule leur
/// empreinte, puis la compare à l'empreinte indexée — elle ne fait JAMAIS confiance à une valeur en base seule.
/// Distinct de l'intégrité FISCALE (chaîne de hashes + ancrage RFC 3161, <see cref="IArchiveVerifier"/>),
/// réservée à un document fiscal.
/// </summary>
public enum GedArchiveIntegrityStatus
{
    /// <summary>Aucun paquet à vérifier : le document n'est pas (encore) rangé dans le coffre (chemin/empreinte absents).</summary>
    NotArchived,

    /// <summary>Les octets re-lus du coffre recalculent EXACTEMENT l'empreinte indexée : intégrité confirmée.</summary>
    Verified,

    /// <summary>L'empreinte recalculée DIFFÈRE de l'empreinte indexée (contenu modifié ou index désynchronisé) : à investiguer.</summary>
    Altered,

    /// <summary>Le paquet (manifest ou une pièce) est INTROUVABLE dans le coffre : la vérification n'a pas pu re-lire les octets.</summary>
    Missing,
}
