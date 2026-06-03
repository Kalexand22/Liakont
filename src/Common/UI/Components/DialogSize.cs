namespace Stratum.Common.UI.Components;

/// <summary>
/// Predefined dialog sizes. When set, overrides the <c>Width</c> parameter.
/// </summary>
public enum DialogSize
{
    /// <summary>400px (confirmations, simple forms).</summary>
    Sm,

    /// <summary>560px — default (standard forms).</summary>
    Md,

    /// <summary>800px (complex forms, previews).</summary>
    Lg,

    /// <summary>1140px (dashboards, wide tables).</summary>
    Xl,

    /// <summary>100% viewport (immersive views).</summary>
    Fullscreen,
}
