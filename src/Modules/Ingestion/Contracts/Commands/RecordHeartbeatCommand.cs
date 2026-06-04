namespace Liakont.Modules.Ingestion.Contracts.Commands;

using Liakont.Agent.Contracts.Transport;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Enregistre un heartbeat d'agent déjà authentifié (F12 §3.2, §4.2) et renvoie la configuration
/// courante. Tous les champs sont posés par l'endpoint à partir de l'identité authentifiée et du
/// corps VALIDÉ : <see cref="AgentId"/> vient de l'identité (jamais du corps) et
/// <see cref="ContractVersion"/> est la version NÉGOCIÉE (en-tête déjà validé par le filtre), pas
/// celle — potentiellement divergente — du corps.
/// </summary>
public sealed record RecordHeartbeatCommand : ICommand<HeartbeatResponseDto>
{
    public required Guid AgentId { get; init; }

    /// <summary>Version de contrat négociée (en-tête <c>X-Contract-Version</c> validé par le filtre).</summary>
    public required string ContractVersion { get; init; }

    /// <summary>Version de l'agent installé (non vide — validée à l'endpoint).</summary>
    public required string AgentVersion { get; init; }

    /// <summary>Horodatage d'émission déclaré par l'agent (UTC).</summary>
    public required DateTime SentAtUtc { get; init; }

    /// <summary>Dernier push réussi déclaré par l'agent (UTC), si connu.</summary>
    public DateTime? LastSuccessfulSyncUtc { get; init; }
}
