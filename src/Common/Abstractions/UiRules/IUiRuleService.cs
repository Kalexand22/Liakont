namespace Stratum.Common.Abstractions.UiRules;

/// <summary>
/// High-level service that discovers all <see cref="IUiRuleProvider{TDto}"/> implementations
/// via DI, evaluates their rules against a DTO instance, and returns a
/// <see cref="UiAttributeSet"/>. Intended for use by Blazor forms and grids to
/// obtain dynamic field attributes on load and after each onChange.
/// </summary>
public interface IUiRuleService<TDto>
{
    /// <summary>
    /// Evaluates all registered UI rules for <typeparamref name="TDto"/> against
    /// the given <paramref name="dto"/> and returns the combined field attributes.
    /// </summary>
    UiAttributeSet Evaluate(TDto dto);
}
