namespace Stratum.Common.Abstractions.FieldChange;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Discovers and executes <see cref="IFieldChangeHandler{T}"/> methods for all
/// fields that changed on an entity. Handles cascade (when an onChange sets a
/// field that itself has an onChange handler).
/// </summary>
public interface IFieldChangeEngine
{
    /// <summary>
    /// Processes all field changes for the given entity and returns the
    /// aggregate result (fields to set + UI attribute overrides).
    /// </summary>
    Task<FieldChangeResult> ProcessChangesAsync<T>(
        T entity,
        IReadOnlySet<string> changedFields,
        IActorContext actor,
        CancellationToken ct = default);
}
