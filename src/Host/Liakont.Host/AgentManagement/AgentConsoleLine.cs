namespace Liakont.Host.AgentManagement;

using System;

/// <summary>
/// Ligne d'agent du parc pour l'écran « Gestion des agents » (WEB09, F10 amendée 2026-06-03 / F12 §4.2).
/// Vue de LISTE alimentée par <c>IAgentQueries</c> (registre système, tenant-scopé) — ne porte JAMAIS de
/// secret (ni clé ni empreinte ; seul le préfixe public). <see cref="IsSilent"/> est calculé par le service
/// d'assemblage à partir du seuil « agent muet » de supervision (F12 §5.2, surchargeable par tenant) — la
/// même source que la règle SUP01 <c>AgentMuteAlertRule</c>, aucune valeur inventée.
/// </summary>
public sealed record AgentConsoleLine
{
    /// <summary>Identifiant de l'agent (clé des actions révoquer / renouveler).</summary>
    public required Guid Id { get; init; }

    /// <summary>Nom lisible de l'agent.</summary>
    public required string Name { get; init; }

    /// <summary>Préfixe public de la clé courante (identifie la clé sans permettre de s'authentifier).</summary>
    public required string KeyPrefix { get; init; }

    /// <summary><c>true</c> si l'agent est révoqué (sa clé est refusée à l'ingestion).</summary>
    public required bool IsRevoked { get; init; }

    /// <summary>
    /// <c>true</c> si l'agent (non révoqué) est MUET depuis plus que le seuil de supervision (F12 §5.2) :
    /// alerte visuelle dans la liste. Un agent révoqué n'est jamais « muet » (il ne parle plus par conception).
    /// </summary>
    public required bool IsSilent { get; init; }

    /// <summary>Dernier heartbeat reçu (UTC), ou <c>null</c> si l'agent ne s'est jamais signalé.</summary>
    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>Dernière version d'agent vue, ou <c>null</c>.</summary>
    public string? Version { get; init; }

    /// <summary>Date d'enregistrement de l'agent (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Libellé d'état affiché et utilisé pour le tri / la recherche / l'export de la colonne « État »
    /// (le rendu en badge coloré est porté par le ColumnTemplate de la page). Révoqué &gt; Muet &gt; Actif.
    /// </summary>
    public string StateLabel => IsRevoked ? "Révoqué" : IsSilent ? "Muet" : "Actif";
}
