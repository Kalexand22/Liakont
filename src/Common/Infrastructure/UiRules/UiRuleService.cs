namespace Stratum.Common.Infrastructure.UiRules;

using Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Discovers all <see cref="IUiRuleProvider{TDto}"/> implementations for
/// <typeparamref name="TDto"/> via DI, collects their rules, and evaluates them
/// through <see cref="IUiRuleEngine"/>. The resulting <see cref="UiAttributeSet"/>
/// tells Blazor components which fields to hide, make read-only, or require.
/// </summary>
/// <remarks>
/// Registered as Scoped — one instance per Blazor circuit / HTTP request.
/// Rule providers are resolved once per evaluation (they are typically singletons
/// returning the same <see cref="UiRule{T}"/> instances).
/// </remarks>
internal sealed class UiRuleService<TDto>(
    IUiRuleEngine engine,
    IEnumerable<IUiRuleProvider<TDto>> providers) : IUiRuleService<TDto>
{
    private readonly List<UiRule<TDto>> _rules = CollectRules(providers);

    public UiAttributeSet Evaluate(TDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return engine.Evaluate(dto, _rules);
    }

    private static List<UiRule<TDto>> CollectRules(IEnumerable<IUiRuleProvider<TDto>> providers)
    {
        var rules = new List<UiRule<TDto>>();
        foreach (var provider in providers)
        {
            rules.AddRange(provider.GetRules());
        }

        return rules;
    }
}
