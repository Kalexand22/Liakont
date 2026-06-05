namespace Liakont.Agent.Updater;

using System;

/// <summary>Arrêt/démarrage du service Windows de l'agent. Couture testable (pas de vrai SCM en test).</summary>
public interface IServiceControl
{
    /// <summary>Arrête le service et attend l'état « arrêté » (l'arrêt propre attend la fin d'un run).</summary>
    /// <param name="serviceName">Nom du service.</param>
    /// <param name="timeout">Délai maximal d'attente de l'arrêt.</param>
    void StopService(string serviceName, TimeSpan timeout);

    /// <summary>Démarre le service et attend l'état « démarré ».</summary>
    /// <param name="serviceName">Nom du service.</param>
    /// <param name="timeout">Délai maximal d'attente du démarrage.</param>
    void StartService(string serviceName, TimeSpan timeout);
}
