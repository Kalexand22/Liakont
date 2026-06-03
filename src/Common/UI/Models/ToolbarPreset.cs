namespace Stratum.Common.UI.Models;

/// <summary>
/// Toolbar configuration preset for <see cref="Components.RichTextEditor"/>.
/// </summary>
public enum ToolbarPreset
{
    /// <summary>Bold, italic, underline, link only.</summary>
    Minimal,

    /// <summary>Formatting, lists, alignment, link, image, clean.</summary>
    Standard,

    /// <summary>All Quill toolbar options including headers, colors, code block, etc.</summary>
    Full,
}
