namespace Stratum.Common.Infrastructure.Collaboration;

using System.Collections.Concurrent;
using Stratum.Common.Abstractions.Collaboration;

internal sealed class CollaborationService : ICollaborationService
{
    private static readonly TimeSpan DefaultFieldLockTtl = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _timeProvider;

    // ── Entity-level presence ────────────────────────────────────

    // Key: "entityType:entityId" → set of (circuitId, user)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _presenceByEntity = new(StringComparer.Ordinal);

    // Reverse index: circuitId → set of entity keys tracked by that circuit
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _entitiesByCircuit = new(StringComparer.Ordinal);

    // ── Field-level focus ────────────────────────────────────────

    // Key: "entityType:entityId:fieldName" → set of (circuitId → FieldFocusEntry)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FieldFocusEntry>> _focusByField = new(StringComparer.Ordinal);

    // Reverse index: circuitId → set of field keys focused by that circuit
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _fieldsByCircuit = new(StringComparer.Ordinal);

    public CollaborationService()
        : this(TimeProvider.System)
    {
    }

    internal CollaborationService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public event Action? OnPresenceChanged;

    public event Action? OnFieldPresenceChanged;

    public TimeSpan FieldLockTtl => DefaultFieldLockTtl;

    public void Track(string entityType, string entityId, string circuitId, string user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        var entityKey = BuildEntityKey(entityType, entityId);

        var circuits = _presenceByEntity.GetOrAdd(entityKey, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
        circuits[circuitId] = user;

        var entities = _entitiesByCircuit.GetOrAdd(circuitId, _ => []);
        entities.Add(entityKey);

        OnPresenceChanged?.Invoke();
    }

    public void Untrack(string circuitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        // Clear field focus first
        ClearFieldFocusInternal(circuitId, fieldName: null);

        if (!_entitiesByCircuit.TryRemove(circuitId, out var entityKeys))
        {
            return;
        }

        var changed = false;

        foreach (var key in entityKeys.Distinct(StringComparer.Ordinal))
        {
            if (_presenceByEntity.TryGetValue(key, out var circuits))
            {
                if (circuits.TryRemove(circuitId, out _))
                {
                    changed = true;
                }

                // Clean up empty entity entries
                if (circuits.IsEmpty)
                {
                    _presenceByEntity.TryRemove(key, out _);
                }
            }
        }

        if (changed)
        {
            OnPresenceChanged?.Invoke();
        }
    }

    public IReadOnlyList<PresenceEntry> GetPresence(string entityType, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        var entityKey = BuildEntityKey(entityType, entityId);

        if (!_presenceByEntity.TryGetValue(entityKey, out var circuits))
        {
            return [];
        }

        return circuits
            .Select(kvp => new PresenceEntry(kvp.Key, kvp.Value))
            .ToList();
    }

    public void SetFieldFocus(string circuitId, string entityType, string entityId, string fieldName, string user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        var fieldKey = BuildFieldKey(entityType, entityId, fieldName);
        var entry = new FieldFocusEntry(circuitId, user, _timeProvider.GetUtcNow());

        var entries = _focusByField.GetOrAdd(fieldKey, _ => new ConcurrentDictionary<string, FieldFocusEntry>(StringComparer.Ordinal));
        entries[circuitId] = entry;

        var fields = _fieldsByCircuit.GetOrAdd(circuitId, _ => []);
        fields.Add(fieldKey);

        OnFieldPresenceChanged?.Invoke();
    }

    public void ClearFieldFocus(string circuitId, string? fieldName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        ClearFieldFocusInternal(circuitId, fieldName);
    }

    public IReadOnlyList<FieldFocusEntry> GetFieldPresence(string entityType, string entityId, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var fieldKey = BuildFieldKey(entityType, entityId, fieldName);

        if (!_focusByField.TryGetValue(fieldKey, out var entries))
        {
            return [];
        }

        return entries.Values.ToList();
    }

    public string? IsFieldLocked(string entityType, string entityId, string fieldName, string circuitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        var fieldKey = BuildFieldKey(entityType, entityId, fieldName);

        if (!_focusByField.TryGetValue(fieldKey, out var entries))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var cutoff = now - FieldLockTtl;

        foreach (var kvp in entries)
        {
            if (string.Equals(kvp.Key, circuitId, StringComparison.Ordinal))
            {
                continue;
            }

            if (kvp.Value.FocusedAt >= cutoff)
            {
                return kvp.Value.User;
            }
        }

        return null;
    }

    public void RenewFieldFocus(string circuitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        if (!_fieldsByCircuit.TryGetValue(circuitId, out var fieldKeys))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        foreach (var key in fieldKeys.Distinct(StringComparer.Ordinal))
        {
            if (_focusByField.TryGetValue(key, out var entries) &&
                entries.TryGetValue(circuitId, out var existing))
            {
                entries[circuitId] = existing with { FocusedAt = now };
            }
        }
    }

    public void PurgeExpiredEntries()
    {
        var cutoff = _timeProvider.GetUtcNow() - FieldLockTtl;
        var changed = false;

        foreach (var (fieldKey, entries) in _focusByField)
        {
            foreach (var (cid, entry) in entries)
            {
                if (entry.FocusedAt < cutoff)
                {
                    if (entries.TryRemove(cid, out _))
                    {
                        changed = true;
                    }
                }
            }

            if (entries.IsEmpty)
            {
                _focusByField.TryRemove(fieldKey, out _);
            }
        }

        if (changed)
        {
            OnFieldPresenceChanged?.Invoke();
        }
    }

    private static string BuildEntityKey(string entityType, string entityId) =>
        string.Concat(entityType, ":", entityId);

    private static string BuildFieldKey(string entityType, string entityId, string fieldName) =>
        string.Concat(entityType, ":", entityId, ":", fieldName);

    private void ClearFieldFocusInternal(string circuitId, string? fieldName)
    {
        if (!_fieldsByCircuit.TryGetValue(circuitId, out var fieldKeys))
        {
            return;
        }

        var changed = false;
        var keysToProcess = fieldName is not null
            ? fieldKeys.Where(k => k.EndsWith(string.Concat(":", fieldName), StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)
            : fieldKeys.Distinct(StringComparer.Ordinal);

        foreach (var key in keysToProcess)
        {
            if (_focusByField.TryGetValue(key, out var entries))
            {
                if (entries.TryRemove(circuitId, out _))
                {
                    changed = true;
                }

                if (entries.IsEmpty)
                {
                    _focusByField.TryRemove(key, out _);
                }
            }
        }

        // If clearing all fields, remove the reverse index entirely
        if (fieldName is null)
        {
            _fieldsByCircuit.TryRemove(circuitId, out _);
        }

        if (changed)
        {
            OnFieldPresenceChanged?.Invoke();
        }
    }
}
