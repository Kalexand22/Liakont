namespace Liakont.Modules.Ingestion.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Révoque un agent du tenant courant (F12 §4.2) : sa clé est immédiatement refusée. Scopé au tenant
/// courant côté handler (un opérateur ne peut révoquer que les agents de son tenant).
/// </summary>
public sealed record RevokeAgentCommand : ICommand
{
    public required Guid AgentId { get; init; }
}
