namespace Liakont.Modules.Pipeline.Contracts;

using System;

/// <summary>
/// Vue en lecture d'une exécution du pipeline (journal <c>pipeline.run_logs</c>, tenant-scopé). Surface
/// de lecture publiée par le module Pipeline, consommée par <c>GET /runs</c> (API01) et la page
/// Traitements (WEB04). Les exécutions sont écrites par PIP01b+ (CHECK/SEND/SYNC) ; PIP01a fournit le
/// contrat et la table.
/// </summary>
public sealed record PipelineRunLogDto
{
    /// <summary>Identifiant de l'exécution.</summary>
    public required Guid Id { get; init; }

    /// <summary>Nature de l'exécution (CHECK / SEND / SYNC).</summary>
    public required PipelineRunType RunType { get; init; }

    /// <summary>Origine de l'exécution (manuelle / planifiée).</summary>
    public required PipelineRunTrigger Trigger { get; init; }

    /// <summary>Début de l'exécution (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Fin de l'exécution (UTC), <c>null</c> tant qu'elle n'est pas clôturée.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Nombre de documents traités au cours de l'exécution.</summary>
    public required int DocumentsProcessed { get; init; }

    /// <summary>Nombre de documents traités avec succès.</summary>
    public required int DocumentsSucceeded { get; init; }

    /// <summary>Nombre de documents en échec.</summary>
    public required int DocumentsFailed { get; init; }

    /// <summary>Détail libre (compteurs additionnels, motif d'arrêt…), <c>null</c> si absent.</summary>
    public string? Detail { get; init; }
}
