namespace Stratum.Common.UI.Models;

/// <summary>
/// UI-layer validation rule for <c>FormBuilderCanvas</c> (UIE02).
/// Decoupled from domain DTOs — conversion happens at the consumer.
/// </summary>
public sealed class FormBuilderValidationRule
{
    /// <summary>Rule type (required, min_length, max_length, min, max, regex, custom).</summary>
    public string Type { get; set; } = "required";

    /// <summary>Optional parameters as JSON string.</summary>
    public string? ParamsJson { get; set; }

    /// <summary>Optional custom error message.</summary>
    public string? ErrorMessage { get; set; }
}
