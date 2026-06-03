namespace Stratum.Common.UI.Models;

/// <summary>
/// Mutable template-level state for <c>FormBuilderCanvas</c> (UIE02).
/// This is a UI-layer model — conversion to/from domain DTOs (FormTemplateDto)
/// happens at the consumer level.
/// </summary>
public sealed class FormBuilderTemplateState
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Machine-readable code (e.g. "demande_occupation").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Template version number.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional entity type binding.</summary>
    public string? EntityType { get; set; }

    /// <summary>Ordered sections in this template.</summary>
    public List<FormBuilderSectionState> Sections { get; set; } = [];
}
