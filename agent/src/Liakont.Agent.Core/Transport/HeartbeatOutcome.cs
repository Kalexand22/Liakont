namespace Liakont.Agent.Core.Transport;

using System;
using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Résultat d'un battement de cœur (POST /api/agent/v1/heartbeat — F12 §3.2). Quand
/// <see cref="Kind"/> vaut <see cref="PlatformResponseKind.Ok"/> : <see cref="Configuration"/> porte
/// la configuration effective renvoyée par la plateforme (planification, période, version attendue) et
/// <see cref="ServerTimeUtc"/> l'heure serveur. Pour toute autre catégorie, l'agent CONSERVE sa
/// configuration locale et réessaie au cycle suivant (échec silencieux — F12 §2.5 : c'est la
/// PLATEFORME qui alerte sur l'absence de heartbeat, jamais l'agent).
/// </summary>
public sealed class HeartbeatOutcome
{
    /// <summary>Crée un résultat de heartbeat.</summary>
    /// <param name="kind">Catégorie de réponse de la plateforme.</param>
    /// <param name="configuration">Configuration effective renvoyée (renseignée uniquement pour une réponse 200).</param>
    /// <param name="serverTimeUtc">Heure serveur (UTC) renvoyée, si disponible.</param>
    /// <param name="reason">Détail / diagnostic, si applicable.</param>
    public HeartbeatOutcome(
        PlatformResponseKind kind,
        AgentConfigurationDto? configuration = null,
        DateTime? serverTimeUtc = null,
        string? reason = null)
    {
        Kind = kind;
        Configuration = configuration;
        ServerTimeUtc = serverTimeUtc;
        Reason = reason;
    }

    /// <summary>Catégorie de réponse de la plateforme.</summary>
    public PlatformResponseKind Kind { get; }

    /// <summary>Configuration effective renvoyée (<c>null</c> hors réponse 200 exploitable).</summary>
    public AgentConfigurationDto? Configuration { get; }

    /// <summary>Heure serveur (UTC) renvoyée par la plateforme, si disponible.</summary>
    public DateTime? ServerTimeUtc { get; }

    /// <summary>Détail / diagnostic.</summary>
    public string? Reason { get; }
}
