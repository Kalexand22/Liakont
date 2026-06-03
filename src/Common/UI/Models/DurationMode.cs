namespace Stratum.Common.UI.Models;

/// <summary>
/// Input mode for the DurationField component.
/// </summary>
public enum DurationMode
{
    /// <summary>Structured input with separate hours/minutes/seconds segments.</summary>
    Structured,

    /// <summary>Free-form text input parsing (e.g., "1h30m", "1:30", "90").</summary>
    FreeForm,
}
