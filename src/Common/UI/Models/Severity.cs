namespace Stratum.Common.UI.Models;

/// <summary>Severity level used by status badges, toasts, and form field feedback.</summary>
public enum Severity
{
    /// <summary>Informational — neutral, blue.</summary>
    Info,

    /// <summary>Success — green.</summary>
    Success,

    /// <summary>Warning — amber.</summary>
    Warning,

    /// <summary>Error — red.</summary>
    Error,

    /// <summary>Neutral — grey, no semantic meaning.</summary>
    Neutral,
}
