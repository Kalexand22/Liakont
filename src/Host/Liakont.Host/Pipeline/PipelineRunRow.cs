namespace Liakont.Host.Pipeline;

using System;
using System.Globalization;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Projection de présentation d'une exécution du pipeline (<see cref="PipelineRunLogDto"/>) pour la
/// page Traitements (WEB04a, journal F10 §2.6). C'est de la PRÉSENTATION pure : libellés français des
/// énumérations et durée lisible. Les valeurs chiffrées (compteurs, horodatages) proviennent VERBATIM
/// du module Pipeline (<c>IPipelineRunQueries</c> / <c>GET /runs</c>) — rien n'est recalculé ni
/// requalifié ici (aucune logique métier dans la page, CLAUDE.md). Toutes les propriétés sont
/// NON-NULLABLES : le tri réflexif de <c>DeclaredListPage</c> remplace un null par <c>string.Empty</c>,
/// si bien qu'une colonne nullable mélangeant null et valeurs typées ferait lever <c>OrderBy</c> sur
/// un type de clé hétérogène. La durée d'une exécution non clôturée est donc rendue « En cours », pas null.
/// </summary>
internal sealed record PipelineRunRow
{
    /// <summary>Identifiant de l'exécution (identité stable de la ligne pour la sélection/dédup).</summary>
    public required Guid Id { get; init; }

    /// <summary>Début de l'exécution (colonne « Date », tri par défaut décroissant).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Nature de l'exécution en clair (colonne « Nature » : Contrôle / Envoi / Synchronisation…).</summary>
    public required string Nature { get; init; }

    /// <summary>Origine de l'exécution en clair (colonne « Déclencheur » : Manuel / Planifié / Ingestion).</summary>
    public required string Trigger { get; init; }

    /// <summary>Durée lisible (colonne « Durée ») ; « En cours » tant que l'exécution n'est pas clôturée.</summary>
    public required string Duration { get; init; }

    /// <summary>Documents traités au cours de l'exécution (colonne « Traités » ; source : DocumentsProcessed du DTO).</summary>
    public required int DocumentsProcessed { get; init; }

    /// <summary>Documents traités avec succès (colonne « Validés »).</summary>
    public required int DocumentsValidated { get; init; }

    /// <summary>Documents en échec (colonne « En échec »).</summary>
    public required int DocumentsFailed { get; init; }

    /// <summary>Détail libre fourni par le pipeline (compteurs additionnels, motif d'arrêt…), « — » si absent.</summary>
    public required string Detail { get; init; }

    /// <summary>Projette un DTO du module Pipeline en ligne de présentation (formatage uniquement).</summary>
    public static PipelineRunRow FromDto(PipelineRunLogDto dto) => new()
    {
        Id = dto.Id,
        StartedAt = dto.StartedAt,
        Nature = RunTypeLabel(dto.RunType),
        Trigger = TriggerLabel(dto.Trigger),
        Duration = FormatDuration(dto.StartedAt, dto.CompletedAt),
        DocumentsProcessed = dto.DocumentsProcessed,
        DocumentsValidated = dto.DocumentsSucceeded,
        DocumentsFailed = dto.DocumentsFailed,
        Detail = string.IsNullOrWhiteSpace(dto.Detail) ? "—" : dto.Detail,
    };

    /// <summary>Libellé opérateur français de la nature d'exécution (source : <see cref="PipelineRunType"/>).</summary>
    public static string RunTypeLabel(PipelineRunType runType) => runType switch
    {
        PipelineRunType.Check => "Contrôle",
        PipelineRunType.Send => "Envoi",
        PipelineRunType.Sync => "Synchronisation",
        PipelineRunType.Aggregate => "Agrégation",
        PipelineRunType.Rectify => "Rectification",
        _ => runType.ToString(),
    };

    /// <summary>Libellé opérateur français de l'origine d'exécution (source : <see cref="PipelineRunTrigger"/>).</summary>
    public static string TriggerLabel(PipelineRunTrigger trigger) => trigger switch
    {
        PipelineRunTrigger.Manual => "Manuel",
        PipelineRunTrigger.Scheduled => "Planifié",
        PipelineRunTrigger.Event => "Ingestion",
        _ => trigger.ToString(),
    };

    /// <summary>
    /// Met en forme la durée écoulée entre le début et la fin d'une exécution. Renvoie « En cours »
    /// tant que l'exécution n'est pas clôturée (<paramref name="completedAt"/> null). Un écart négatif
    /// (dérive d'horloge entre deux nœuds) est borné à zéro plutôt que d'afficher une durée absurde.
    /// </summary>
    public static string FormatDuration(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        if (completedAt is not { } end)
        {
            return "En cours";
        }

        var elapsed = end - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var fr = CultureInfo.GetCultureInfo("fr-FR");
        if (elapsed.TotalSeconds < 60)
        {
            return string.Format(fr, "{0} s", (int)elapsed.TotalSeconds);
        }

        if (elapsed.TotalMinutes < 60)
        {
            return string.Format(fr, "{0} min {1} s", (int)elapsed.TotalMinutes, elapsed.Seconds);
        }

        return string.Format(fr, "{0} h {1} min", (int)elapsed.TotalHours, elapsed.Minutes);
    }
}
