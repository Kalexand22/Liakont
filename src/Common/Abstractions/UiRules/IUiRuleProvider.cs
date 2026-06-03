namespace Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Provides a set of declarative UI rules for <typeparamref name="TDto"/>.
/// Implementations live in the Application layer of each module
/// (e.g., <c>Company/Application/UiRules/CompanyDtoUiRules.cs</c>).
/// The UI rule engine discovers all registered providers via DI and evaluates
/// their rules against a DTO instance to produce a <see cref="UiAttributeSet"/>.
/// </summary>
public interface IUiRuleProvider<TDto>
{
    /// <summary>
    /// Returns the UI rules for <typeparamref name="TDto"/>.
    /// </summary>
    IEnumerable<UiRule<TDto>> GetRules();
}
