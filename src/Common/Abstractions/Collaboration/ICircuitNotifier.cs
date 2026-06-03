namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Sends notifications to Blazor circuits that are watching a given entity.
/// Implementation lives in the UI layer (Blazor-specific).
/// </summary>
public interface ICircuitNotifier
{
    /// <summary>
    /// Notify the given circuits that an entity was changed.
    /// </summary>
    Task NotifyEntityChangedAsync(EntityChangedEvent evt, IReadOnlyList<PresenceEntry> circuits);
}
