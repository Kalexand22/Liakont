namespace Liakont.Modules.Ingestion.Contracts.Commands;

using Liakont.Agent.Contracts.Transport;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Enregistre un heartbeat d'agent déjà authentifié (F12 §3.2, §4.2) et renvoie la configuration
/// courante. L'<see cref="AgentId"/> provient de l'identité authentifiée (posée par le filtre
/// d'authentification agent), jamais du corps de la requête.
/// </summary>
public sealed record RecordHeartbeatCommand : ICommand<HeartbeatResponseDto>
{
    public required Guid AgentId { get; init; }

    public required HeartbeatRequestDto Heartbeat { get; init; }
}
