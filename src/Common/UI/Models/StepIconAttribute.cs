namespace Stratum.Common.UI.Models;

/// <summary>
/// Specifies an icon name for an enum value when rendered as a step in <c>StratumEnumStepper</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class StepIconAttribute(string iconName) : Attribute
{
    public string IconName { get; } = iconName ?? throw new ArgumentNullException(nameof(iconName));
}
