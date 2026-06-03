namespace Stratum.Common.UI.Models;

/// <summary>
/// Cascading context published by <c>FormField</c> so nested input components can
/// pick up the correct <c>id</c>, <c>aria-required</c>, and <c>aria-describedby</c> values.
/// </summary>
public sealed class FormFieldContext
{
    /// <summary>The <c>id</c> that the inner input element must carry.</summary>
    public string FieldId { get; init; } = string.Empty;

    /// <summary>
    /// The <c>id</c> of the error or help-text element, for use as <c>aria-describedby</c>.
    /// <c>null</c> when neither error nor help text is present.
    /// </summary>
    public string? DescribedBy { get; init; }

    /// <summary>Whether the field is marked as required.</summary>
    public bool Required { get; init; }

    /// <summary>Whether an error message is currently displayed.</summary>
    public bool HasError { get; init; }

    /// <summary>Whether the field is read-only (driven by dynamic UI rules).</summary>
    public bool ReadOnly { get; init; }
}
