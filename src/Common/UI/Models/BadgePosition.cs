namespace Stratum.Common.UI.Models;

/// <summary>
/// Anchor position for the <c>Badge</c> overlay relative to its child element.
/// </summary>
public enum BadgePosition
{
    /// <summary>Top-right corner (default, standard for notifications).</summary>
    TopRight,

    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Bottom-right corner.</summary>
    BottomRight,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft,
}
