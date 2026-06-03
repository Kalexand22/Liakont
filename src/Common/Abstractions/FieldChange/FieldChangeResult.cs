namespace Stratum.Common.Abstractions.FieldChange;

using Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Result returned by a field-change handler method. Contains field values to
/// propagate back to the entity and optional UI attribute overrides.
/// </summary>
public sealed record FieldChangeResult
{
    private FieldChangeResult()
    {
    }

    /// <summary>
    /// Fields that should be updated on the entity as a consequence of the change.
    /// Keys are field names, values are the new values to set.
    /// </summary>
    public IReadOnlyDictionary<string, object?> FieldsToSet { get; private init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Optional UI attribute overrides to apply after the field change
    /// (e.g., hide/show fields, toggle required state).
    /// </summary>
    public UiAttributeSet? UiAttributes { get; private init; }

    /// <summary>
    /// Creates an empty result (no fields to set, no UI changes).
    /// </summary>
    public static FieldChangeResult Empty() => new();

    /// <summary>
    /// Creates a result that sets one or more fields on the entity.
    /// </summary>
    public static FieldChangeResult WithFields(IReadOnlyDictionary<string, object?> fieldsToSet)
    {
        ArgumentNullException.ThrowIfNull(fieldsToSet);
        return new() { FieldsToSet = fieldsToSet };
    }

    /// <summary>
    /// Creates a result with both field updates and UI attribute overrides.
    /// </summary>
    public static FieldChangeResult WithFieldsAndUi(
        IReadOnlyDictionary<string, object?> fieldsToSet,
        UiAttributeSet uiAttributes)
    {
        ArgumentNullException.ThrowIfNull(fieldsToSet);
        ArgumentNullException.ThrowIfNull(uiAttributes);
        return new() { FieldsToSet = fieldsToSet, UiAttributes = uiAttributes };
    }

    /// <summary>
    /// Merges multiple results into one. Field updates from later results
    /// overwrite earlier ones for the same key. UI attributes are merged.
    /// </summary>
    public static FieldChangeResult Merge(IEnumerable<FieldChangeResult> results)
    {
        var mergedFields = new Dictionary<string, object?>();
        UiAttributeSet? mergedUi = null;

        foreach (var result in results)
        {
            foreach (var kvp in result.FieldsToSet)
            {
                mergedFields[kvp.Key] = kvp.Value;
            }

            if (result.UiAttributes is not null)
            {
                mergedUi = mergedUi is null
                    ? result.UiAttributes
                    : UiAttributeSet.Merge(mergedUi, result.UiAttributes);
            }
        }

        return new FieldChangeResult
        {
            FieldsToSet = mergedFields,
            UiAttributes = mergedUi,
        };
    }
}
