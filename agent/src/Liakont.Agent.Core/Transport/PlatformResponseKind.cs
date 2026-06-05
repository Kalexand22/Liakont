namespace Liakont.Agent.Core.Transport;

/// <summary>
/// Catégorie de réponse de la plateforme à une requête de l'agent, dérivée du code HTTP (F12 §3.3).
/// L'agent applique une règle MÉCANIQUE par catégorie ; il n'interprète aucun état métier.
/// </summary>
public enum PlatformResponseKind
{
    /// <summary>200 — la requête a abouti (résultats par document dans la réponse).</summary>
    Ok = 1,

    /// <summary>400 — payload non conforme au contrat : terminal, pas de retry (l'élément est mis en erreur et signalé).</summary>
    BadRequest = 2,

    /// <summary>401/403 — clé API invalide ou révoquée : arrêt immédiat avec erreur explicite.</summary>
    Unauthorized = 3,

    /// <summary>413 — lot trop gros : re-découpe du lot.</summary>
    PayloadTooLarge = 4,

    /// <summary>426 — version d'agent non supportée : déclenche l'auto-update (AGT04).</summary>
    UpgradeRequired = 5,

    /// <summary>429/5xx — surcharge ou indisponibilité : backoff exponentiel, les éléments restent en file.</summary>
    Throttled = 6,

    /// <summary>Coupure réseau / délai dépassé / réponse inattendue : les éléments restent en file (rien perdu).</summary>
    TransportError = 7,
}
