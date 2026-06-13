namespace Liakont.Host.Clients;

using Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>Résultat de l'enregistrement du premier agent (clé complète remise UNE fois).</summary>
internal sealed record ClientAgentKeyResult(
    ClientActionStatus Status,
    AgentKeyIssuedDto? IssuedKey = null,
    string? Message = null);
