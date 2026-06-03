namespace Stratum.Common.UI.Components;

/// <summary>
/// Configuration for a single dashlet inside a <see cref="DashboardLayout"/>.
/// Positions and sizes are expressed in grid columns and rows.
/// </summary>
public sealed record DashletConfig
{
    /// <summary>Unique identifier — must match the corresponding <see cref="Dashlet.ConfigId"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Starting column (0-based).</summary>
    public int X { get; init; }

    /// <summary>Starting row (0-based).</summary>
    public int Y { get; init; }

    /// <summary>Width in grid columns. Default: 4.</summary>
    public int W { get; init; } = 4;

    /// <summary>Height in grid rows. Default: 2.</summary>
    public int H { get; init; } = 2;

    /// <summary>Minimum width in columns. <c>null</c> = no minimum.</summary>
    public int? MinW { get; init; }

    /// <summary>Minimum height in rows. <c>null</c> = no minimum.</summary>
    public int? MinH { get; init; }

    /// <summary>When <c>true</c>, the dashlet cannot be moved or resized.</summary>
    public bool Locked { get; init; }
}
