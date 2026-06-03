namespace Stratum.Common.UI.Models;

/// <summary>
/// Specifies a color token name for an enum value when rendered as a step in <c>StratumEnumStepper</c>.
/// <para>
/// Valid values: <c>"primary"</c>, <c>"success"</c>, <c>"warning"</c>, <c>"error"</c>,
/// <c>"info"</c>, <c>"neutral"</c>. Other values will produce no visual effect.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class StepColorAttribute(string colorName) : Attribute
{
    public string ColorName { get; } = colorName ?? throw new ArgumentNullException(nameof(colorName));
}
