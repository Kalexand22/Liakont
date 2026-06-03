namespace Stratum.Common.UI.Models;

/// <summary>
/// Mutable field state for <c>FormBuilderCanvas</c> (UIE02).
/// </summary>
public sealed class FormBuilderFieldState
{
    /// <summary>Field identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Machine-readable code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Field type key (text, number, date, single_select, etc.).</summary>
    public string FieldType { get; set; } = "text";

    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Optional help text shown below the field.</summary>
    public string? HelpText { get; set; }

    /// <summary>Optional placeholder text.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Sort order within the section.</summary>
    public int SortOrder { get; set; }

    /// <summary>Whether this field is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Default value as JSON string.</summary>
    public string? DefaultValueJson { get; set; }

    /// <summary>Validation rules for this field.</summary>
    public List<FormBuilderValidationRule> ValidationRules { get; set; } = [];

    /// <summary>Optional visibility condition DSL JSON.</summary>
    public string? VisibilityConditionJson { get; set; }

    /// <summary>Options JSON (for select/multi_select field types).</summary>
    public string? OptionsJson { get; set; }
}
