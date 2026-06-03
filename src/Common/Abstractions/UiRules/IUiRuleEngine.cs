namespace Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Evaluates <see cref="UiRule{T}"/> expressions against a DTO instance and
/// produces a <see cref="UiAttributeSet"/> that the Blazor UI consumes to
/// dynamically show/hide, enable/disable, and require fields.
/// </summary>
public interface IUiRuleEngine
{
    /// <summary>
    /// Evaluates the given rules against <paramref name="dto"/> and returns
    /// the resulting field attributes.
    /// </summary>
    UiAttributeSet Evaluate<T>(T dto, IEnumerable<UiRule<T>> rules);
}
