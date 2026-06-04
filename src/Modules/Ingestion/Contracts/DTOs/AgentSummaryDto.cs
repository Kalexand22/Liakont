namespace Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Vue de lecture d'un agent pour la console et la supervision (F12 §4.2, §5). N'expose JAMAIS la
/// clé (ni le clair ni l'empreinte) : seul le préfixe public est visible.
/// </summary>
public sealed record AgentSummaryDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string KeyPrefix { get; init; }

    public required bool IsRevoked { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Dernier heartbeat reçu (UTC), ou <c>null</c> si l'agent ne s'est jamais signalé.</summary>
    public DateTimeOffset? LastSeenAtUtc { get; init; }

    /// <summary>Dernière version d'agent vue, ou <c>null</c>.</summary>
    public string? LastAgentVersion { get; init; }
}
