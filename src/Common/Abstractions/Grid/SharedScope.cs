namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Defines the sharing scope for a saved filter.
/// </summary>
public enum SharedScope
{
    /// <summary>Only visible to the owning user.</summary>
    None = 0,

    /// <summary>Visible to all users.</summary>
    Everyone = 1,
}
