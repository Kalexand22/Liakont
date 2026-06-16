namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Niveau de preuve d'une signature (ADR-0027 §2 ; F17 §2 ; eIDAS art. 25/26). <c>[Flags]</c> décrivant
/// l'ENSEMBLE des niveaux RÉELLEMENT activés sur un compte — JAMAIS un maximum ordonné (INV-SIGPROV-4) :
/// un compte peut offrir <c>QES</c> sans <c>AES</c>, ou <c>SES | QES</c>. Déduire « AES disponible car
/// niveau ≥ AES » ferait demander un niveau NON licencié — la capacité reste la seule source de vérité.
/// <see cref="Recorded"/> (acceptation enregistrée SANS signature, défaut conforme ADR-0024) est TOUJOURS
/// implicitement disponible. Le test « niveau supporté » est une APPARTENANCE à l'ensemble
/// (<see cref="SignatureProviderCapabilities.Supports(SignatureLevel)"/>), jamais une comparaison ordonnée.
/// Aucun niveau eIDAS n'est imposé par le produit : le niveau requis par besoin est un PARAMÉTRAGE TENANT
/// (F17 §7 ; CLAUDE.md n°2/3). Valeurs en puissances de deux distinctes, <c>None = 0</c>.
/// </summary>
[Flags]
public enum SignatureLevel
{
    /// <summary>Aucun niveau déclaré.</summary>
    None = 0,

    /// <summary>Acceptation ENREGISTRÉE sans signature électronique (défaut conforme ADR-0024).</summary>
    Recorded = 1,

    /// <summary>Signature électronique SIMPLE (SES, eIDAS art. 25).</summary>
    SES = 2,

    /// <summary>Signature électronique AVANCÉE (AES, eIDAS art. 26).</summary>
    AES = 4,

    /// <summary>Signature électronique QUALIFIÉE (QES, eIDAS art. 3 §12).</summary>
    QES = 8,
}
