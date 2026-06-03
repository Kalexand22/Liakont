namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Raised after a successful save to notify other circuits that an entity was modified.
/// </summary>
public sealed record EntityChangedEvent(
    string EntityType,
    string EntityId,
    string ChangedBy,
    DateTimeOffset ChangedAt);
