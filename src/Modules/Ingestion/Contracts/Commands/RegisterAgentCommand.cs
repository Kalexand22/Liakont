namespace Liakont.Modules.Ingestion.Contracts.Commands;

using Liakont.Modules.Ingestion.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Enregistre un nouvel agent pour le tenant courant (F12 §4.2) et génère sa clé API. Le tenant est
/// résolu côté handler via le contexte tenant (jamais un paramètre client — anti-fuite cross-tenant).
/// Renvoie la clé COMPLÈTE, affichée une seule fois.
/// </summary>
public sealed record RegisterAgentCommand : ICommand<AgentKeyIssuedDto>
{
    public required string Name { get; init; }
}
