namespace Liakont.Modules.Ingestion.Domain.Entities;

/// <summary>
/// Entrée APPEND-ONLY de l'historique des heartbeats (F12 §4.2). Conservée 90 jours pour la
/// supervision (dead-man's switch, F12 §5) — ce n'est PAS une piste d'audit légale : sa purge par
/// rétention est légitime (assurée hors de ce module, par un job de supervision). Aucun chemin
/// d'update n'existe : un heartbeat reçu est un fait, jamais modifié.
/// </summary>
public sealed class HeartbeatLogEntry
{
    private HeartbeatLogEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid AgentId { get; private set; }

    public string TenantId { get; private set; } = string.Empty;

    public string ContractVersion { get; private set; } = string.Empty;

    public string AgentVersion { get; private set; } = string.Empty;

    /// <summary>Horodatage d'émission déclaré par l'agent (UTC).</summary>
    public DateTimeOffset SentAtUtc { get; private set; }

    /// <summary>Dernier push réussi déclaré par l'agent (UTC), si connu.</summary>
    public DateTimeOffset? LastSuccessfulSyncUtc { get; private set; }

    /// <summary>Horodatage de réception côté plateforme (UTC) — référence pour le dead-man's switch.</summary>
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static HeartbeatLogEntry Create(
        Guid agentId,
        string tenantId,
        string contractVersion,
        string agentVersion,
        DateTimeOffset sentAtUtc,
        DateTimeOffset? lastSuccessfulSyncUtc,
        DateTimeOffset receivedAtUtc)
    {
        return new HeartbeatLogEntry
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            TenantId = tenantId,
            ContractVersion = contractVersion,
            AgentVersion = agentVersion,
            SentAtUtc = sentAtUtc,
            LastSuccessfulSyncUtc = lastSuccessfulSyncUtc,
            ReceivedAtUtc = receivedAtUtc,
        };
    }

    public static HeartbeatLogEntry Reconstitute(
        Guid id,
        Guid agentId,
        string tenantId,
        string contractVersion,
        string agentVersion,
        DateTimeOffset sentAtUtc,
        DateTimeOffset? lastSuccessfulSyncUtc,
        DateTimeOffset receivedAtUtc)
    {
        return new HeartbeatLogEntry
        {
            Id = id,
            AgentId = agentId,
            TenantId = tenantId,
            ContractVersion = contractVersion,
            AgentVersion = agentVersion,
            SentAtUtc = sentAtUtc,
            LastSuccessfulSyncUtc = lastSuccessfulSyncUtc,
            ReceivedAtUtc = receivedAtUtc,
        };
    }
}
