namespace Liakont.Agent.Contracts.Transport;

using System;

/// <summary>
/// Réponse de la plateforme à un heartbeat (F12 §3.2) : l'heure serveur et la configuration courante
/// de l'agent.
/// </summary>
public sealed class HeartbeatResponseDto
{
    /// <summary>Crée une réponse de heartbeat.</summary>
    /// <param name="serverTimeUtc">Heure serveur (UTC) — référence d'horloge pour l'agent.</param>
    /// <param name="configuration">Configuration courante de l'agent.</param>
    public HeartbeatResponseDto(DateTime serverTimeUtc, AgentConfigurationDto configuration)
    {
        ServerTimeUtc = serverTimeUtc;
        Configuration = configuration;
    }

    /// <summary>Heure serveur (UTC).</summary>
    public DateTime ServerTimeUtc { get; }

    /// <summary>Configuration courante de l'agent.</summary>
    public AgentConfigurationDto Configuration { get; }
}
