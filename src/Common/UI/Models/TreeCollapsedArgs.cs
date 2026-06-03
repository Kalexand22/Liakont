namespace Stratum.Common.UI.Models;

/// <summary>Args for tree node collapse.</summary>
public sealed class TreeCollapsedArgs
{
    public TreeCollapsedArgs(object? value, string? text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>The data item being collapsed.</summary>
    public object? Value { get; }

    /// <summary>The display text of the collapsed node.</summary>
    public string? Text { get; }
}
