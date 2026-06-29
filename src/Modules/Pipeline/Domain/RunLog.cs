namespace Liakont.Modules.Pipeline.Domain;

using System;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Trace d'une exécution du pipeline (PIP01) — modèle d'écriture du journal <c>pipeline.run_logs</c>
/// (base DU TENANT, scopé par la connexion — blueprint §7). Une exécution est OUVERTE au démarrage
/// (<see cref="Start"/>) puis CLÔTURÉE (<see cref="Complete"/>) avec ses compteurs. AUCUNE logique de
/// pipeline ici (pas de CHECK/SEND/SYNC) : l'agrégat est rempli par PIP01b+ et lu via
/// <see cref="Contracts.Queries.IPipelineRunQueries"/> (API01 / WEB04). PIP01a fournit le modèle + la table.
/// </summary>
public sealed class RunLog
{
    private RunLog()
    {
    }

    /// <summary>Identifiant de l'exécution.</summary>
    public Guid Id { get; private set; }

    /// <summary>Nature de l'exécution (CHECK / SEND / SYNC).</summary>
    public PipelineRunType RunType { get; private set; }

    /// <summary>Origine de l'exécution (manuelle / planifiée).</summary>
    public PipelineRunTrigger Trigger { get; private set; }

    /// <summary>Début de l'exécution (UTC).</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Fin de l'exécution (UTC), <c>null</c> tant qu'elle n'est pas clôturée.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Nombre de documents traités.</summary>
    public int DocumentsProcessed { get; private set; }

    /// <summary>Nombre de documents traités avec succès.</summary>
    public int DocumentsSucceeded { get; private set; }

    /// <summary>Nombre de documents en échec.</summary>
    public int DocumentsFailed { get; private set; }

    /// <summary>Nombre de documents DIFFÉRÉS (contenu pas encore stagé / dépôt asynchrone — transitoire, en cours
    /// d'émission). Distinct des ignorés : un différé sera émis au prochain cycle, alors qu'un ignoré ne partira pas (RBF07).</summary>
    public int DocumentsDeferred { get; private set; }

    /// <summary>Nombre de documents en attente d'une ACTION OPÉRATEUR (émetteur non publié / table TVA non reposée).
    /// HOLD distinct du différé transitoire : repris seulement après correction du paramétrage (RBF07).</summary>
    public int DocumentsHeld { get; private set; }

    /// <summary>Détail libre (compteurs additionnels, motif d'arrêt…), <c>null</c> si absent.</summary>
    public string? Detail { get; private set; }

    /// <summary>Vrai quand l'exécution est clôturée (a une fin).</summary>
    public bool IsCompleted => CompletedAt.HasValue;

    /// <summary>Ouvre une exécution (compteurs à zéro, sans fin).</summary>
    /// <param name="runType">Nature de l'exécution.</param>
    /// <param name="trigger">Origine de l'exécution.</param>
    /// <param name="startedAt">Début de l'exécution (UTC), fourni par l'appelant (déterminisme).</param>
    /// <returns>Une exécution ouverte.</returns>
    public static RunLog Start(PipelineRunType runType, PipelineRunTrigger trigger, DateTimeOffset startedAt)
    {
        return new RunLog
        {
            Id = Guid.NewGuid(),
            RunType = runType,
            Trigger = trigger,
            StartedAt = startedAt,
            CompletedAt = null,
            DocumentsProcessed = 0,
            DocumentsSucceeded = 0,
            DocumentsFailed = 0,
            DocumentsDeferred = 0,
            DocumentsHeld = 0,
            Detail = null,
        };
    }

    /// <summary>
    /// Clôture l'exécution avec ses compteurs. La fin ne peut pas précéder le début ; les compteurs
    /// sont positifs. Idempotence non requise : une exécution n'est clôturée qu'une fois.
    /// </summary>
    /// <param name="completedAt">Fin de l'exécution (UTC).</param>
    /// <param name="documentsProcessed">Documents traités (≥ 0).</param>
    /// <param name="documentsSucceeded">Documents en succès (≥ 0).</param>
    /// <param name="documentsFailed">Documents en échec (≥ 0).</param>
    /// <param name="detail">Détail libre (facultatif).</param>
    /// <param name="documentsDeferred">Documents différés — en cours d'émission (≥ 0). Facultatif : seul SEND
    /// en produit ; les autres exécutions (CHECK/SYNC/agrégation) laissent 0 (RBF07).</param>
    /// <param name="documentsHeld">Documents en attente d'une action opérateur (≥ 0). Facultatif : seul SEND
    /// en produit (RBF07).</param>
    public void Complete(
        DateTimeOffset completedAt,
        int documentsProcessed,
        int documentsSucceeded,
        int documentsFailed,
        string? detail = null,
        int documentsDeferred = 0,
        int documentsHeld = 0)
    {
        if (completedAt < StartedAt)
        {
            throw new ArgumentException(
                "La fin d'une exécution de pipeline ne peut pas précéder son début.", nameof(completedAt));
        }

        RequireNonNegative(documentsProcessed, nameof(documentsProcessed));
        RequireNonNegative(documentsSucceeded, nameof(documentsSucceeded));
        RequireNonNegative(documentsFailed, nameof(documentsFailed));
        RequireNonNegative(documentsDeferred, nameof(documentsDeferred));
        RequireNonNegative(documentsHeld, nameof(documentsHeld));

        CompletedAt = completedAt;
        DocumentsProcessed = documentsProcessed;
        DocumentsSucceeded = documentsSucceeded;
        DocumentsFailed = documentsFailed;
        DocumentsDeferred = documentsDeferred;
        DocumentsHeld = documentsHeld;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }

    private static void RequireNonNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Un compteur d'exécution ne peut pas être négatif.");
        }
    }
}
