namespace Stratum.Common.Abstractions.FieldChange;

/// <summary>
/// Blazor-facing service that components call when a form field value changes.
/// Wraps <see cref="IFieldChangeEngine"/> with actor context resolution and
/// provides a simpler API for UI integration.
/// </summary>
/// <typeparam name="T">The DTO/entity type being edited in the form.</typeparam>
public interface IFieldChangeNotifier<T>
{
    /// <summary>
    /// Notifies the engine that one or more fields have changed on the entity.
    /// Returns field values to propagate back and optional UI attribute overrides.
    /// </summary>
    /// <param name="entity">The current state of the entity being edited.</param>
    /// <param name="changedFields">The names of the fields that changed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregate result with fields to set and UI attribute overrides.</returns>
    Task<FieldChangeResult> NotifyAsync(T entity, IReadOnlySet<string> changedFields, CancellationToken ct = default);

    /// <summary>
    /// Convenience overload for a single field change.
    /// </summary>
    Task<FieldChangeResult> NotifyAsync(T entity, string changedField, CancellationToken ct = default);
}
