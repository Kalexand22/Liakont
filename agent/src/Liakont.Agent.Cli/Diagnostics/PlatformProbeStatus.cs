namespace Liakont.Agent.Cli.Diagnostics;

/// <summary>
/// Diagnostic d'un appel « heartbeat à blanc » à la plateforme (F12 §2.1, §3.3, commande
/// <c>test-api</c>). Mappe les codes de réponse du contrat d'ingestion sur une cause lisible.
/// </summary>
internal enum PlatformProbeStatus
{
    /// <summary>La plateforme a répondu favorablement (2xx) : URL et clé valides.</summary>
    Ok = 0,

    /// <summary>URL injoignable (DNS, réseau, TLS, délai dépassé) — aucune réponse HTTP.</summary>
    Unreachable = 1,

    /// <summary>Clé API invalide (401).</summary>
    InvalidKey = 2,

    /// <summary>Clé API révoquée ou non autorisée (403).</summary>
    RevokedKey = 3,

    /// <summary>Version du contrat de l'agent non supportée (426 — une mise à jour est requise).</summary>
    UpgradeRequired = 4,

    /// <summary>Réponse HTTP inattendue (autre code) — à examiner.</summary>
    UnexpectedResponse = 5,
}
