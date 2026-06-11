namespace Liakont.Modules.Supervision.Contracts.DTOs;

using System.Collections.Generic;

/// <summary>
/// Détail de supervision d'un tenant (SUP02, page <c>/supervision/{tenantId}</c>) : ses agents, ses alertes
/// ACTIVES (acquittables) et l'historique récent des alertes, plus les compteurs de documents. Lecture seule,
/// agrégée par le module Supervision (seul contexte cross-tenant du produit — CLAUDE.md n°9).
/// </summary>
public sealed record TenantSupervisionDetailDto
{
    /// <summary>Identifiant (slug) du tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Nom d'affichage du tenant.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Agents du tenant (révoqués inclus), pour l'état de la flotte.</summary>
    public required IReadOnlyList<AgentStatusDto> Agents { get; init; }

    /// <summary>Alertes actives du tenant (acquittables), déclenchement le plus récent d'abord.</summary>
    public required IReadOnlyList<AlertDto> ActiveAlerts { get; init; }

    /// <summary>Historique récent des alertes (actives et résolues), déclenchement le plus récent d'abord.</summary>
    public required IReadOnlyList<AlertDto> RecentAlerts { get; init; }

    /// <summary>Documents en état <c>Blocked</c>.</summary>
    public required int BlockedDocumentCount { get; init; }

    /// <summary>Documents en état <c>RejectedByPa</c>.</summary>
    public required int RejectedByPaDocumentCount { get; init; }

    /// <summary>Documents en attente d'envoi (état <c>ReadyToSend</c>).</summary>
    public required int PendingDocumentCount { get; init; }
}
