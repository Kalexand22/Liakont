namespace Liakont.Modules.Supervision.Contracts.DTOs;

using System;

/// <summary>
/// État de lecture d'un agent pour le dashboard de supervision (SUP02) : projection slim de la vue d'agent
/// (Ingestion) limitée à ce que la supervision affiche — jamais la clé (ni le clair ni l'empreinte). La
/// taille de file de push N'Y FIGURE PAS : son producteur (télémétrie heartbeat persistée) n'existe pas
/// encore (gelé SUP01c) — l'afficher serait une donnée fabriquée (CLAUDE.md n°7, faux-vert de supervision).
/// </summary>
public sealed record AgentStatusDto
{
    /// <summary>Nom de l'agent.</summary>
    public required string Name { get; init; }

    /// <summary>Agent révoqué (désactivé) : ne compte plus comme « muet ».</summary>
    public required bool IsRevoked { get; init; }

    /// <summary>Dernier heartbeat reçu (UTC), ou <c>null</c> si l'agent ne s'est jamais signalé.</summary>
    public DateTimeOffset? LastSeenAtUtc { get; init; }

    /// <summary>Dernière version d'agent vue, ou <c>null</c>.</summary>
    public string? LastAgentVersion { get; init; }
}
