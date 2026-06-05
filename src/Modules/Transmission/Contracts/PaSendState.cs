namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// État d'un envoi vers la PA. Couvre les trois familles d'erreur de F05 §4.1 (réseau re-tentable
/// vs rejet métier non re-tentable vs erreur silencieuse 200 + errors[]) plus le cas « capacité
/// absente » propre à l'abstraction produit (acceptance PAA01).
/// </summary>
public enum PaSendState
{
    /// <summary>Créé sans envoi (<c>send_after_import = false</c>) — non facturable (F05 §2).</summary>
    New = 0,

    /// <summary>Envoi en cours côté PA.</summary>
    Sending = 1,

    /// <summary>Émis / accepté par la PA.</summary>
    Issued = 2,

    /// <summary>
    /// Rejeté par la PA : 4xx OU réponse 200 contenant <c>errors[]</c> (erreur silencieuse, F05 §4.1).
    /// PAS de retry automatique : les <see cref="PaSendResult.Errors"/> détaillent le motif.
    /// </summary>
    RejectedByPa = 3,

    /// <summary>Erreur technique (réseau, 5xx, timeout) — re-tentable au prochain run (F05 §4.1).</summary>
    TechnicalError = 4,

    /// <summary>
    /// La PA ne prend pas en charge la capacité requise : le produit n'est pas bloqué, l'appel
    /// retourne un résultat typé (<see cref="PaSendResult.CapabilityNotSupported"/>) au lieu de lever.
    /// </summary>
    CapabilityNotSupported = 5,
}
