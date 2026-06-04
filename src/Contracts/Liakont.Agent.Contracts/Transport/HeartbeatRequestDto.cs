namespace Liakont.Agent.Contracts.Transport;

using System;

/// <summary>
/// Battement de cœur émis par l'agent (POST /api/agent/v1/heartbeat — F12 §3.2). La plateforme
/// persiste l'état (rétention 90 jours, PIV05) et répond une <see cref="AgentConfigurationDto"/>.
/// </summary>
public sealed class HeartbeatRequestDto
{
    /// <summary>Crée un battement de cœur.</summary>
    /// <param name="contractVersion">Version du contrat émise par l'agent.</param>
    /// <param name="agentVersion">Version de l'agent installé (pour la politique de mise à jour de flotte).</param>
    /// <param name="sentAtUtc">Horodatage d'émission (UTC).</param>
    /// <param name="lastSuccessfulSyncUtc">Dernier push réussi (UTC), si connu.</param>
    public HeartbeatRequestDto(
        string contractVersion,
        string agentVersion,
        DateTime sentAtUtc,
        DateTime? lastSuccessfulSyncUtc = null)
    {
        ContractVersion = contractVersion;
        AgentVersion = agentVersion;
        SentAtUtc = sentAtUtc;
        LastSuccessfulSyncUtc = lastSuccessfulSyncUtc;
    }

    /// <summary>Version du contrat émise par l'agent.</summary>
    public string ContractVersion { get; }

    /// <summary>Version de l'agent installé.</summary>
    public string AgentVersion { get; }

    /// <summary>Horodatage d'émission (UTC).</summary>
    public DateTime SentAtUtc { get; }

    /// <summary>Dernier push réussi (UTC), si connu.</summary>
    public DateTime? LastSuccessfulSyncUtc { get; }
}
