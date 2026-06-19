namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// État du résultat d'une demande de signature (<see cref="SignatureRequestResult"/>). Une capacité
/// absente n'est PAS une exception : c'est l'état <see cref="CapabilityNotSupported"/>, jamais un
/// blocage du produit (ADR-0027 §3 ; modèle <c>PaSendState</c>).
/// </summary>
public enum SignatureRequestState
{
    /// <summary>Demande SOUMISE au fournisseur ; la complétion sera signalée plus tard (webhook/polling).</summary>
    Submitted,

    /// <summary>Demande COMPLÉTÉE de façon synchrone (capteur sur place).</summary>
    Completed,

    /// <summary>Demande REJETÉE par le fournisseur (entrée invalide côté fournisseur).</summary>
    Rejected,

    /// <summary>Capacité ou niveau non pris en charge — résultat typé, jamais une exception.</summary>
    CapabilityNotSupported,

    /// <summary>Erreur technique re-tentable (réseau, 5xx).</summary>
    TechnicalError,
}
