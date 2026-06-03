namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Tracks which users (circuits) are currently viewing a given entity,
/// and which field each user is focused on within that entity.
/// Singleton — shared across all Blazor circuits.
/// </summary>
public interface ICollaborationService
{
    /// <summary>
    /// Raised when presence changes for any entity.
    /// Subscribers should re-check GetPresence for the entities they care about.
    /// </summary>
    event Action? OnPresenceChanged;

    /// <summary>
    /// Raised when field focus changes for any entity.
    /// Subscribers should re-check GetFieldPresence for the fields they care about.
    /// </summary>
    event Action? OnFieldPresenceChanged;

    /// <summary>
    /// Time-to-live for field locks. A focus entry older than this is considered expired.
    /// Default: 60 seconds. Exposed for testability.
    /// </summary>
    TimeSpan FieldLockTtl { get; }

    /// <summary>
    /// Register a circuit as present on an entity.
    /// </summary>
    void Track(string entityType, string entityId, string circuitId, string user);

    /// <summary>
    /// Remove all presence entries for a circuit (e.g. on disconnect/navigation).
    /// Also clears any field focus held by that circuit.
    /// </summary>
    void Untrack(string circuitId);

    /// <summary>
    /// Get all users currently present on an entity.
    /// </summary>
    IReadOnlyList<PresenceEntry> GetPresence(string entityType, string entityId);

    /// <summary>
    /// Mark a circuit as focused on a specific field of an entity.
    /// Replaces any previous field focus for the same circuit on the same entity.
    /// </summary>
    void SetFieldFocus(string circuitId, string entityType, string entityId, string fieldName, string user);

    /// <summary>
    /// Clear a circuit's field focus. If fieldName is specified, only clears that field;
    /// otherwise clears all field focus for the circuit.
    /// </summary>
    void ClearFieldFocus(string circuitId, string? fieldName = null);

    /// <summary>
    /// Get all users currently focused on a specific field of an entity.
    /// </summary>
    IReadOnlyList<FieldFocusEntry> GetFieldPresence(string entityType, string entityId, string fieldName);

    /// <summary>
    /// Check whether a field is currently locked by another circuit.
    /// A field is locked when another circuit has focus on it and the focus
    /// is within the TTL window (not expired). Returns the locking user's
    /// name when locked, or <c>null</c> when the field is free.
    /// </summary>
    string? IsFieldLocked(string entityType, string entityId, string fieldName, string circuitId);

    /// <summary>
    /// Renew the TTL for all field focus entries held by a circuit.
    /// Called periodically by the heartbeat mechanism to prevent focus expiration.
    /// </summary>
    void RenewFieldFocus(string circuitId);

    /// <summary>
    /// Remove all field focus entries whose <see cref="FieldFocusEntry.FocusedAt"/> is older
    /// than <see cref="FieldLockTtl"/>. Called periodically by the cleanup background service.
    /// </summary>
    void PurgeExpiredEntries();
}

/// <summary>
/// Represents a single user's presence on an entity.
/// </summary>
public sealed record PresenceEntry(string CircuitId, string User);
