namespace Stratum.Common.Abstractions.UiRules;

using System.Linq.Expressions;

/// <summary>
/// Fluent builder that accumulates predicates and produces a <see cref="UiRule{T}"/>.
/// Instances are created via <see cref="Rule.For{T}"/>.
/// </summary>
public sealed class UiRuleBuilder<T>
{
    private readonly Expression<Func<T, object>> _fieldExpression;
    private Expression<Func<T, bool>>? _hiddenWhen;
    private Expression<Func<T, bool>>? _readOnlyWhen;
    private Expression<Func<T, bool>>? _requiredWhen;
    private Expression<Func<T, string>>? _domainFilter;

    internal UiRuleBuilder(Expression<Func<T, object>> fieldExpression)
    {
        _fieldExpression = fieldExpression;
    }

    /// <summary>
    /// Implicit conversion so that the builder can be used directly in collections
    /// without calling <see cref="Build"/> explicitly.
    /// </summary>
    public static implicit operator UiRule<T>(UiRuleBuilder<T> builder) => builder.Build();

    /// <summary>
    /// The field is hidden when <paramref name="predicate"/> evaluates to <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if HiddenWhen was already set.</exception>
    public UiRuleBuilder<T> HiddenWhen(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (_hiddenWhen is not null)
        {
            throw new InvalidOperationException("HiddenWhen has already been set on this rule builder.");
        }

        _hiddenWhen = predicate;
        return this;
    }

    /// <summary>
    /// The field is read-only when <paramref name="predicate"/> evaluates to <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if ReadOnlyWhen was already set.</exception>
    public UiRuleBuilder<T> ReadOnlyWhen(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (_readOnlyWhen is not null)
        {
            throw new InvalidOperationException("ReadOnlyWhen has already been set on this rule builder.");
        }

        _readOnlyWhen = predicate;
        return this;
    }

    /// <summary>
    /// The field is required when <paramref name="predicate"/> evaluates to <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if RequiredWhen was already set.</exception>
    public UiRuleBuilder<T> RequiredWhen(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (_requiredWhen is not null)
        {
            throw new InvalidOperationException("RequiredWhen has already been set on this rule builder.");
        }

        _requiredWhen = predicate;
        return this;
    }

    /// <summary>
    /// Applies a domain filter expression to restrict lookup/select values for this field.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if WithDomainFilter was already set.</exception>
    public UiRuleBuilder<T> WithDomainFilter(Expression<Func<T, string>> domainFilter)
    {
        ArgumentNullException.ThrowIfNull(domainFilter);
        if (_domainFilter is not null)
        {
            throw new InvalidOperationException("DomainFilter has already been set on this rule builder.");
        }

        _domainFilter = domainFilter;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="UiRule{T}"/> from the accumulated predicates.
    /// </summary>
    public UiRule<T> Build() =>
        new()
        {
            FieldExpression = _fieldExpression,
            HiddenWhen = _hiddenWhen,
            ReadOnlyWhen = _readOnlyWhen,
            RequiredWhen = _requiredWhen,
            DomainFilter = _domainFilter,
        };
}
