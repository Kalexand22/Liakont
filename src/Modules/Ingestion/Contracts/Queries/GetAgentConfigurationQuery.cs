namespace Liakont.Modules.Ingestion.Contracts.Queries;

using Liakont.Agent.Contracts.Transport;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Renvoie la configuration courante d'un agent authentifié (GET /api/agent/v1/configuration,
/// F12 §3.2). Le <see cref="TenantId"/> provient de l'identité authentifiée (jamais du client).
/// </summary>
public sealed record GetAgentConfigurationQuery : IQuery<AgentConfigurationDto>
{
    public required string TenantId { get; init; }
}
