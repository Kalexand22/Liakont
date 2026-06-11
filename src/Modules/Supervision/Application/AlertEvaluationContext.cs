namespace Liakont.Modules.Supervision.Application;

using System;

/// <summary>
/// Contexte d'évaluation d'une règle pour UN tenant. <see cref="NowUtc"/> est l'instant unique du cycle
/// (fourni par le moteur depuis un <c>TimeProvider</c>) : toutes les règles d'un même cycle comparent
/// leurs seuils (ex. « &gt; 24 h ») au MÊME instant — jamais un <c>DateTimeOffset.UtcNow</c> capté règle
/// par règle, testable et reproductible.
/// </summary>
public sealed class AlertEvaluationContext
{
    public AlertEvaluationContext(string tenantId, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        TenantId = tenantId;
        NowUtc = nowUtc;
    }

    /// <summary>Tenant évalué (slug). Les requêtes de la règle sont tenant-scopées par la connexion du scope.</summary>
    public string TenantId { get; }

    /// <summary>Instant de référence du cycle d'évaluation (UTC).</summary>
    public DateTimeOffset NowUtc { get; }
}
