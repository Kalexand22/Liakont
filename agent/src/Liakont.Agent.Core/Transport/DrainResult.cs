namespace Liakont.Agent.Core.Transport;

/// <summary>
/// Compteurs et issue d'un drainage (un appel <see cref="QueueDrainer.DrainOnce"/>). Sert à la
/// journalisation et au heartbeat (AGT03). Une issue <see cref="StoppedBy"/> non nulle indique que le
/// drainage s'est arrêté tôt (clé invalide, mise à jour requise, indisponibilité) en laissant les
/// éléments restants en file — rien n'est perdu.
/// </summary>
public sealed class DrainResult
{
    /// <summary>Documents poussés et marqués « en cours » (accusé reçu, en attente d'état terminal — ADR-0012).</summary>
    public int DocumentsInProgress { get; internal set; }

    /// <summary>Documents acquittés (statut terminal Processed) et purgés de la file.</summary>
    public int DocumentsAcknowledged { get; internal set; }

    /// <summary>Documents rejetés (terminal) : purgés et signalés à l'opérateur, jamais re-poussés.</summary>
    public int DocumentsRejected { get; internal set; }

    /// <summary>Documents « reçus mais non rangés » renvoyés au prochain push (statut non terminal).</summary>
    public int DocumentsResent { get; internal set; }

    /// <summary>Documents mis en erreur (400 / document trop volumineux) : signalés, pas re-tentés.</summary>
    public int DocumentsErrored { get; internal set; }

    /// <summary>PDF poussés avec succès et purgés de la file.</summary>
    public int PdfsAcknowledged { get; internal set; }

    /// <summary>PDF mis en erreur (introuvable / trop volumineux) : signalés, pas re-tentés.</summary>
    public int PdfsErrored { get; internal set; }

    /// <summary>Catégorie de réponse ayant arrêté le drainage (clé invalide, update, surcharge), ou <c>null</c> si terminé.</summary>
    public PlatformResponseKind? StoppedBy { get; internal set; }

    /// <summary>Le drainage a été interrompu par une demande d'arrêt (les éléments restent en file).</summary>
    public bool Cancelled { get; internal set; }
}
