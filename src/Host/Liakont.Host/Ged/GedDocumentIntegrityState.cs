namespace Liakont.Host.Ged;

/// <summary>
/// Verdict d'intégrité présenté sur la fiche document GED (GED09b, F19 §6.7). Distingue l'intégrité GED
/// (re-lecture write-once vs <c>content_hash</c>) du rattachement FISCAL, dont l'intégrité de référence est la
/// chaîne de hashes + ancrage (IArchiveVerifier, TRK06) — surfacée, jamais ré-appliquée au GED.
/// </summary>
public enum GedDocumentIntegrityState
{
    /// <summary>Re-lecture du coffre conforme à l'empreinte indexée : intégrité confirmée.</summary>
    Verified,

    /// <summary>Empreinte recalculée divergente : contenu modifié ou index désynchronisé.</summary>
    Altered,

    /// <summary>Paquet (manifest ou pièce) introuvable dans le coffre.</summary>
    Missing,

    /// <summary>Document pas (encore) rangé dans le coffre.</summary>
    NotArchived,

    /// <summary>Document fiscal lié : intégrité portée par la chaîne fiscale (coffre fiscal), pas par la re-lecture GED.</summary>
    FiscalLinked,
}
