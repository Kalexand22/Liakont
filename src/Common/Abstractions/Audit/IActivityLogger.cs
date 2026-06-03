namespace Stratum.Common.Abstractions.Audit;

/// <summary>
/// Records business-level activities to the persistent activity trail.
/// </summary>
/// <remarks>
/// <strong>INV-AUDIT-002:</strong> Implementations must never propagate write failures to callers.
/// An activity write failure must not roll back or fail the business transaction.
/// </remarks>
public interface IActivityLogger
{
    /// <summary>
    /// Records a business-level activity for a given entity.
    /// </summary>
    /// <param name="entityType">Short logical entity name, e.g. <c>"Party"</c>.</param>
    /// <param name="entityId">String representation of the entity primary key.</param>
    /// <param name="activityType">Activity classification, e.g. <c>"created"</c>, <c>"status_changed"</c>.</param>
    /// <param name="description">Human-readable description of the activity.</param>
    /// <param name="actorId">Identity of the user or system that triggered the activity.</param>
    /// <param name="metadata">Optional structured data for additional context (serialized as JSONB).</param>
    /// <param name="companyId">Optional company scope for multi-tenant isolation.</param>
    /// <param name="cancellationToken">Propagates cancellation but must not cause data loss on cancel.</param>
    Task LogActivityAsync(
        string entityType,
        string entityId,
        string activityType,
        string description,
        string actorId,
        object? metadata = null,
        Guid? companyId = null,
        CancellationToken cancellationToken = default);
}
