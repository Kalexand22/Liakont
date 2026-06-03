namespace Stratum.Common.Abstractions.Audit;

/// <summary>
/// Writes field-level audit changes to the persistent audit trail.
/// </summary>
/// <remarks>
/// <para>
/// Multiple calls with the same <paramref name="entryId"/> group all field changes
/// from a single business operation (e.g., one command handler invocation).
/// </para>
/// <para>
/// <strong>INV-AUDIT-002:</strong> Implementations must never propagate write failures to callers.
/// An audit write failure must not roll back or fail the business transaction.
/// </para>
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Records a single field-level change.
    /// </summary>
    /// <param name="entryId">
    /// Groups all field changes from one business operation.
    /// Generate once per command/handler invocation and reuse across all fields.
    /// </param>
    /// <param name="entityType">Short logical entity name, e.g. <c>"Party"</c>.</param>
    /// <param name="entityId">String representation of the entity primary key.</param>
    /// <param name="fieldName">Property or column name that changed.</param>
    /// <param name="oldValue">Previous value, or <c>null</c> for creates.</param>
    /// <param name="newValue">New value, or <c>null</c> for deletes.</param>
    /// <param name="actorId">Identity of the user or system that triggered the change.</param>
    /// <param name="cancellationToken">Propagates cancellation but must not cause data loss on cancel.</param>
    Task WriteChangeAsync(
        Guid entryId,
        string entityType,
        string entityId,
        string fieldName,
        object? oldValue,
        object? newValue,
        string actorId,
        CancellationToken cancellationToken = default);
}
