namespace Liakont.Modules.Ingestion.Contracts.Commands;

using Liakont.Modules.Ingestion.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Fait pivoter la clé API d'un agent du tenant courant (F12 §4.2) : l'ancienne clé cesse d'être
/// valide, une nouvelle est émise (affichée une seule fois). Scopé au tenant courant côté handler.
/// </summary>
public sealed record RotateAgentKeyCommand : ICommand<AgentKeyIssuedDto>
{
    public required Guid AgentId { get; init; }
}
