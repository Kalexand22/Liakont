namespace Stratum.Common.UI.Components;

/// <summary>Drawing mode for <see cref="DrawingField"/>.</summary>
public enum DrawingMode
{
    /// <summary>Freehand drawing — pen color and width configurable via toolbar.</summary>
    Freehand,

    /// <summary>Signature — thin black line on white background; color and width selectors hidden.</summary>
    Signature,
}
