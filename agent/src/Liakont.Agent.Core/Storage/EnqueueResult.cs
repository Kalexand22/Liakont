namespace Liakont.Agent.Core.Storage;

/// <summary>Résultat d'un <see cref="LocalQueue.Enqueue"/> : enfilé, ou déjà présent (idempotence).</summary>
public enum EnqueueResult
{
    /// <summary>L'élément a été ajouté à la file.</summary>
    Enqueued = 1,

    /// <summary>Un élément de même clé (kind, source_reference, payload_hash) est déjà en file : aucun doublon créé.</summary>
    AlreadyQueued = 2,
}
