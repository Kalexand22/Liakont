namespace Liakont.Agent.Core.Storage;

/// <summary>État d'un élément dans <c>push_queue</c>. L'élément ne quitte la file qu'après ACK terminal (AGT02).</summary>
public enum QueueItemStatus
{
    /// <summary>En attente de push.</summary>
    Pending = 1,

    /// <summary>Push en cours (« accepted » reçu, en attente de confirmation terminale — ADR-0012).</summary>
    InProgress = 2,

    /// <summary>En erreur (échec non terminal signalé au heartbeat ; pas de retry infini).</summary>
    Error = 3,
}
