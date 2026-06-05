namespace Liakont.Agent.Updater;

using System;

/// <summary>
/// Juge si la NOUVELLE version a redémarré SAINEMENT (ADR-0013) : service en cours d'exécution ET
/// heartbeat local frais (l'agent touche un marqueur à chaque heartbeat sain). C'est le critère de
/// rollback : si la santé n'est pas confirmée dans le budget imparti, l'updater restaure l'ancienne
/// version. Couture testable (pas d'attente réelle en test).
/// </summary>
public interface IServiceHealthProbe
{
    /// <summary>
    /// Attend, jusqu'à <paramref name="timeout"/>, la confirmation que le service est sain après
    /// redémarrage. Renvoie <c>false</c> si la santé n'est pas confirmée dans le délai.
    /// </summary>
    /// <param name="serviceName">Nom du service.</param>
    /// <param name="heartbeatMarkerPath">Marqueur de heartbeat local (sa fraîcheur prouve un cycle sain).</param>
    /// <param name="timeout">Budget d'attente.</param>
    /// <returns><c>true</c> si le redémarrage est confirmé sain.</returns>
    bool WaitUntilHealthy(string serviceName, string heartbeatMarkerPath, TimeSpan timeout);
}
