namespace Liakont.Modules.Supervision.Contracts.DTOs;

using System;

/// <summary>
/// Ligne de la vue d'ensemble multi-tenants du dashboard de supervision (SUP02) : agrégat en LECTURE SEULE
/// d'un tenant — alertes actives, état des agents et compteurs de documents. Renseigné par le module
/// Supervision, SEUL contexte cross-tenant du produit (CLAUDE.md n°9). <see cref="ReadFailed"/> signale un
/// tenant dont la lecture a échoué : il reste VISIBLE (ne jamais masquer un tenant — ce serait précisément
/// la panne silencieuse que la supervision existe pour détecter), avec des compteurs à zéro.
/// </summary>
public sealed record TenantSupervisionRowDto
{
    /// <summary>Identifiant (slug) du tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Nom d'affichage du tenant.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Nombre d'alertes actives (toutes gravités) du tenant.</summary>
    public required int ActiveAlertCount { get; init; }

    /// <summary>Nombre d'alertes actives de gravité Critique.</summary>
    public required int CriticalAlertCount { get; init; }

    /// <summary>Gravité la plus forte parmi les alertes actives (<c>Critical</c> / <c>Warning</c>), ou <c>null</c> si aucune.</summary>
    public string? WorstSeverity { get; init; }

    /// <summary>Nombre d'agents enregistrés (révoqués inclus) pour ce tenant.</summary>
    public required int AgentCount { get; init; }

    /// <summary>Heartbeat le plus récent parmi les agents du tenant (UTC), ou <c>null</c> si aucun ne s'est signalé.</summary>
    public DateTimeOffset? LastAgentSeenUtc { get; init; }

    /// <summary>Documents en état <c>Blocked</c>.</summary>
    public required int BlockedDocumentCount { get; init; }

    /// <summary>Documents en état <c>RejectedByPa</c>.</summary>
    public required int RejectedByPaDocumentCount { get; init; }

    /// <summary>Documents en attente d'envoi (état <c>ReadyToSend</c>).</summary>
    public required int PendingDocumentCount { get; init; }

    /// <summary>
    /// La lecture de ce tenant a échoué (base injoignable, etc.) : la ligne est affichée en avertissement,
    /// compteurs à zéro — JAMAIS masquée (un tenant absent du tableau serait une panne silencieuse).
    /// </summary>
    public bool ReadFailed { get; init; }
}
