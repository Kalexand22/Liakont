namespace Stratum.Common.UI.Models;

/// <summary>
/// Mutable section state for <c>FormBuilderCanvas</c> (UIE02).
/// </summary>
public sealed class FormBuilderSectionState
{
    /// <summary>Section identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Machine-readable code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Sort order within the template.</summary>
    public int SortOrder { get; set; }

    /// <summary>Optional visibility condition DSL JSON.</summary>
    public string? VisibilityConditionJson { get; set; }

    /// <summary>Fields in this section.</summary>
    public List<FormBuilderFieldState> Fields { get; set; } = [];
}
