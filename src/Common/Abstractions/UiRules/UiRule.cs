namespace Stratum.Common.Abstractions.UiRules;

using System.Linq.Expressions;

/// <summary>
/// A declarative UI rule binding a field on <typeparamref name="T"/> to conditional
/// visibility, editability, requiredness, and domain filtering. Rules are defined
/// as C# lambda expressions in <see cref="IUiRuleProvider{T}"/> implementations
/// and evaluated at runtime by the UI rule engine.
/// </summary>
/// <remarks>
/// Use <see cref="Rule.For{T}"/> to build instances via the fluent API rather than
/// constructing directly.
/// </remarks>
public sealed record UiRule<T>
{
    /// <summary>
    /// Expression identifying the target field (e.g., <c>x => x.Discount</c>).
    /// </summary>
    public required Expression<Func<T, object>> FieldExpression { get; init; }

    /// <summary>
    /// When this predicate evaluates to <c>true</c> the field is hidden from the UI.
    /// </summary>
    public Expression<Func<T, bool>>? HiddenWhen { get; init; }

    /// <summary>
    /// When this predicate evaluates to <c>true</c> the field is rendered read-only.
    /// </summary>
    public Expression<Func<T, bool>>? ReadOnlyWhen { get; init; }

    /// <summary>
    /// When this predicate evaluates to <c>true</c> the field is marked as required.
    /// </summary>
    public Expression<Func<T, bool>>? RequiredWhen { get; init; }

    /// <summary>
    /// Optional expression returning a domain filter string to restrict
    /// lookup/select values for this field (e.g., <c>x => $"status == '{x.Status}'"</c>).
    /// </summary>
    public Expression<Func<T, string>>? DomainFilter { get; init; }
}
