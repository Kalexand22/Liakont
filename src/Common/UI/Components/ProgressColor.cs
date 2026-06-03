namespace Stratum.Common.UI.Components;

/// <summary>Color variant for progress indicators (<see cref="LinearProgress"/> and <see cref="CircularProgress"/>).</summary>
public enum ProgressColor
{
    /// <summary>Primary brand color (default).</summary>
    Default,

    /// <summary>Informational (sky blue).</summary>
    Info,

    /// <summary>Positive outcome (green).</summary>
    Success,

    /// <summary>Attention needed (amber).</summary>
    Warning,

    /// <summary>Error or danger (red).</summary>
    Danger,
}
