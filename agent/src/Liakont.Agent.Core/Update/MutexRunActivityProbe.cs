namespace Liakont.Agent.Core.Update;

using System;
using Liakont.Agent.Core.Hosting;

/// <summary>
/// Sonde l'état d'un run via le verrou inter-process partagé (<see cref="InterProcessRunLock"/>, mutex
/// <c>Global\LiakontAgentRun</c>, AGT01/AGT05). Tente une acquisition NON bloquante puis relâche
/// aussitôt : si le verrou est détenu, un run est en cours. Au moindre doute (exception, verrou
/// inaccessible), répond « en cours » — garantie fail-safe « jamais d'update pendant un run ».
/// </summary>
public sealed class MutexRunActivityProbe : IRunActivityProbe
{
    private readonly string _mutexName;

    /// <summary>Crée une sonde sur le mutex de run partagé.</summary>
    /// <param name="mutexName">Nom du mutex (défaut : <see cref="InterProcessRunLock.DefaultMutexName"/>).</param>
    public MutexRunActivityProbe(string? mutexName = null)
    {
        _mutexName = string.IsNullOrWhiteSpace(mutexName) ? InterProcessRunLock.DefaultMutexName : mutexName!;
    }

    /// <inheritdoc/>
    public bool IsRunInProgress()
    {
        try
        {
            using (InterProcessRunLock? heldLock = InterProcessRunLock.TryAcquire(TimeSpan.Zero, _mutexName))
            {
                // Verrou obtenu (heldLock != null) → aucun run ne le détient → pas de run en cours.
                // Le bloc using le relâche immédiatement (la sonde ne sérialise rien, elle observe).
                return heldLock == null;
            }
        }
        catch (Exception)
        {
            // Impossible de trancher (mutex inaccessible) → on suppose un run en cours (fail-safe).
            return true;
        }
    }
}
