namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Marker interface for commands that modify an entity and should trigger
/// collaborative-editing notifications to other circuits viewing the same entity.
/// </summary>
public interface IEntityChangeCommand
{
    /// <summary>Entity type identifier (e.g. "Quote", "SalesOrder").</summary>
    string EntityType { get; }

    /// <summary>Entity identifier (e.g. the primary key as string).</summary>
    string EntityId { get; }
}
