namespace Stratum.Common.UI.Models;

/// <summary>
/// Discriminator for <see cref="LogicNode"/> — a node is either a logical
/// group (AND/OR combinator with children) or a leaf condition (field +
/// operator + value).
/// </summary>
public enum LogicNodeType
{
    /// <summary>AND/OR group containing child nodes.</summary>
    Group,

    /// <summary>Leaf condition: variable + operator + value.</summary>
    Condition,
}
