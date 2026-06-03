namespace Stratum.Common.Abstractions.Domain;

/// <summary>
/// Convention marker for entities that support soft-delete.
/// Infrastructure uses this interface to filter deleted records from queries.
/// Deletion is signalled by a non-null <see cref="DeletedAt"/> value.
/// <para>
/// <strong>Mutation contract (enforced per module, not by this interface):</strong>
/// Every implementing entity must expose domain methods <c>Delete(DateTimeOffset at)</c>
/// and <c>Restore()</c> that set/clear <c>DeletedAt</c> and raise the appropriate domain event.
/// Direct property assignment from outside the entity is not permitted.
/// </para>
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}
